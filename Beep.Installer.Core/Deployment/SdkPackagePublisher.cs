using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Deployment;

public sealed class SdkPackagePublishOptions
{
    public string PackagePath { get; init; } = "";
    public string Source { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public bool DryRun { get; init; }
    public ISecretProvider SecretProvider { get; init; } = new CompositeSecretProvider();
    public Func<SdkPackagePublishInvocation, SdkPackagePublishToolResult>? ToolRunner { get; init; }
}

public sealed class SdkPackagePublishResult
{
    public string PackagePath { get; init; } = "";
    public string Source { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public bool DryRun { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public bool ApiKeyWasSecretReference { get; init; }
    public string CommandLine { get; init; } = "";
    public string ToolVersion { get; init; } = "";
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class SdkPackagePublishInvocation
{
    public string ToolPath { get; init; } = "dotnet";
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = "";
    public string CommandLine => Quote(ToolPath) + " " + string.Join(" ", Arguments.Select(argument =>
        argument.StartsWith("--api-key", StringComparison.OrdinalIgnoreCase) ? argument : Quote(argument)));

    private static string Quote(string value)
        => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
}

public sealed class SdkPackagePublishToolResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public string ToolVersion { get; init; } = "";
}

public sealed class SdkPackagePublisher
{
    public const string ReportFileName = "sdk-package-publish.json";

    public SdkPackagePublishResult Publish(SdkPackagePublishOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var packagePath = FullPath(options.PackagePath);
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory, "sdk-publish")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        if (string.IsNullOrWhiteSpace(options.PackagePath))
            diagnostics.Add(Error("BI2610", "PackagePath", "SDK package path is required."));
        else if (!File.Exists(packagePath))
            diagnostics.Add(Error("BI2611", "PackagePath", $"SDK package does not exist: {packagePath}"));

        if (string.IsNullOrWhiteSpace(options.Source))
            diagnostics.Add(Error("BI2612", "Source", "NuGet package source/feed is required."));

        var apiKeyWasSecretReference = SecretReference.IsReference(options.ApiKey);
        var resolvedApiKey = options.ApiKey;
        if (!string.IsNullOrWhiteSpace(options.ApiKey) && apiKeyWasSecretReference)
        {
            if (!SecretReference.TryParse(options.ApiKey, out var reference, out var parseError))
            {
                diagnostics.Add(Error("BI2613", "ApiKey", parseError ?? "SDK publish API key secret reference is invalid."));
            }
            else
            {
                var resolved = options.SecretProvider.Resolve(reference);
                if (!resolved.Success)
                    diagnostics.Add(Error("BI2614", "ApiKey", resolved.Error ?? "SDK publish API key secret reference could not be resolved."));
                resolvedApiKey = resolved.Value ?? "";
            }
        }

        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return Complete(packagePath, options.Source, outputDirectory, options.DryRun, apiKeyWasSecretReference, "", "", 1, "", "", diagnostics);

        var invocation = CreateInvocation(packagePath, options.Source, resolvedApiKey);
        if (options.DryRun)
            return Complete(packagePath, options.Source, outputDirectory, true, apiKeyWasSecretReference, Redact(invocation.CommandLine, resolvedApiKey), "", 0, "Dry run: SDK package publish command was validated but not executed.", "", diagnostics);

        var run = options.ToolRunner?.Invoke(invocation) ?? RunDotnet(invocation);
        if (run.ExitCode != 0)
            diagnostics.Add(Error("BI2615", "dotnet nuget push", $"SDK package publish failed with exit code {run.ExitCode}: {FirstNonEmptyLine(run.StandardError, run.StandardOutput)}"));

        return Complete(
            packagePath,
            options.Source,
            outputDirectory,
            false,
            apiKeyWasSecretReference,
            Redact(invocation.CommandLine, resolvedApiKey),
            run.ToolVersion,
            run.ExitCode,
            Redact(run.StandardOutput, resolvedApiKey),
            Redact(run.StandardError, resolvedApiKey),
            diagnostics);
    }

    private static SdkPackagePublishInvocation CreateInvocation(string packagePath, string source, string apiKey)
    {
        var args = new List<string> { "nuget", "push", packagePath, "--source", source, "--skip-duplicate" };
        if (!string.IsNullOrWhiteSpace(apiKey))
            args.AddRange(new[] { "--api-key", apiKey });
        return new SdkPackagePublishInvocation
        {
            WorkingDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory,
            Arguments = args
        };
    }

    private static SdkPackagePublishToolResult RunDotnet(SdkPackagePublishInvocation invocation)
    {
        var start = new ProcessStartInfo(invocation.ToolPath)
        {
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in invocation.Arguments)
            start.ArgumentList.Add(argument);

        // MSBuild-driven verbs leave a full set of worker nodes running unless told not to, and
        // those nodes inherit the redirected handles below -- which is how the identical code in
        // HeadlessSdkQualificationRunner used to hang forever with dotnet already exited.
        if (invocation.Arguments.Count > 0 && MsBuildDrivenVerbs.Contains(invocation.Arguments[0]))
            start.ArgumentList.Add("-nodeReuse:false");
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        // This used to be a fourth hand-rolled process runner: unbounded WaitForExit, unbounded
        // Task.WaitAll, ExitCode read unconditionally. BeepDM's InstallHelpers.RunProcess is the
        // one that gets all of it right -- concurrent drain, bounded wait, process-tree kill on
        // overrun, and never a thrown exception -- so use it rather than grow another variant.
        var run = TheTechIdea.Beep.Installer.InstallHelpers.RunProcess(start, (int)PublishTimeout.TotalMilliseconds);

        if (!run.Started)
            return new SdkPackagePublishToolResult { ExitCode = -1, StandardError = run.Error };

        if (run.TimedOut)
            return new SdkPackagePublishToolResult
            {
                ExitCode = -1,
                StandardOutput = run.StandardOutput,
                StandardError = $"'{invocation.CommandLine}' did not finish within " +
                                $"{PublishTimeout.TotalMinutes:0} minutes and was terminated."
            };

        return new SdkPackagePublishToolResult
        {
            ExitCode = run.ExitCode,
            StandardOutput = run.StandardOutput,
            StandardError = run.StandardError
        };
    }

    /// <summary>A pack or a push against a cold cache is slow; this is a wedge backstop, not a budget.</summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Verbs that drive MSBuild and therefore accept (and need) <c>-nodeReuse:false</c>.</summary>
    private static readonly HashSet<string> MsBuildDrivenVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "build", "pack", "restore", "publish", "msbuild" };

    private static SdkPackagePublishResult Complete(
        string packagePath,
        string source,
        string outputDirectory,
        bool dryRun,
        bool apiKeyWasSecretReference,
        string commandLine,
        string toolVersion,
        int exitCode,
        string standardOutput,
        string standardError,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var success = exitCode == 0 && diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var result = new SdkPackagePublishResult
        {
            PackagePath = packagePath,
            Source = source,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            DryRun = dryRun,
            Success = success,
            ExitCode = success ? 0 : exitCode == 0 ? 1 : exitCode,
            ApiKeyWasSecretReference = apiKeyWasSecretReference,
            CommandLine = commandLine,
            ToolVersion = toolVersion,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Diagnostics = diagnostics
        };
        WriteReport(result);
        return result;
    }

    public static void WriteReport(SdkPackagePublishResult result)
    {
        Directory.CreateDirectory(result.OutputDirectory);
        File.WriteAllText(result.ReportPath, JsonSerializer.Serialize(result, SdkPackagePublishJsonContext.Default.SdkPackagePublishResult) + Environment.NewLine);
    }

    private static string FullPath(string path)
        => string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

    private static string FirstNonEmptyLine(params string[] values)
        => values.SelectMany(value => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "No process output was captured.";

    private static string Redact(string value, string secret)
        => string.IsNullOrEmpty(secret) ? value : value.Replace(secret, "[redacted]", StringComparison.Ordinal);

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SdkPackagePublishResult))]
internal sealed partial class SdkPackagePublishJsonContext : JsonSerializerContext;
