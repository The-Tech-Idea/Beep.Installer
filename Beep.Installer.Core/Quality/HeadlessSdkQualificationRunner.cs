using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class HeadlessSdkQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string SdkProjectPath { get; init; } = "";
    public string PackageVersion { get; init; } = "";
}

public sealed class HeadlessSdkQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string SdkProjectPath { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string PackagePath { get; init; } = "";
    public string ConsumerProjectPath { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<HeadlessSdkQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class HeadlessSdkQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

public sealed class HeadlessSdkQualificationRunner
{
    public const string ReportFileName = "headless-sdk-qualification.json";
    public const string PackageId = "TheTechIdea.Beep.Installer.Sdk";
    public const string DefaultPackageVersion = "1.0.0";

    public HeadlessSdkQualificationReport Run(HeadlessSdkQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "headless-sdk-qualification")
            : options.OutputDirectory);
        var packageVersion = string.IsNullOrWhiteSpace(options.PackageVersion) ? DefaultPackageVersion : options.PackageVersion.Trim();
        var sdkProjectPath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.SdkProjectPath) ? FindSdkProjectPath() : options.SdkProjectPath);
        var feedDirectory = Path.Combine(outputDirectory, "local-feed");
        var consumerDirectory = Path.Combine(outputDirectory, "external-consumer");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(feedDirectory);
        Directory.CreateDirectory(consumerDirectory);

        var scenarios = new List<HeadlessSdkQualificationScenario>();
        var directPlan = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
        {
            ProjectPath = projectPath,
            Strict = true
        });

        scenarios.Add(Scenario(
            "sdk-direct-plan",
            "The source SDK facade can validate and compile the qualification project before packaging.",
            directPlan.Diagnostics,
            outputDirectory,
            directPlan.Success ? 0 : directPlan.ExitCode,
            directPlan.Success ? $"Plan hash: {directPlan.PlanHash}" : "Direct SDK plan failed."));

        var packageMetadataDiagnostics = ValidateSdkProjectMetadata(sdkProjectPath);
        scenarios.Add(Scenario(
            "sdk-package-metadata",
            "The SDK project carries public NuGet package identity and package readme metadata.",
            packageMetadataDiagnostics,
            outputDirectory));

        var pack = RunDotnet(Path.GetDirectoryName(sdkProjectPath) ?? Environment.CurrentDirectory, "pack", sdkProjectPath, "--nologo", "--output", feedDirectory, $"/p:PackageVersion={packageVersion}", "/p:WarningLevel=0");
        scenarios.Add(Scenario(
            "dotnet-pack",
            "The headless SDK packs into a local NuGet feed using the canonical Core project.",
            DiagnosticsFromProcess("BI26010", "dotnet pack", pack),
            outputDirectory,
            pack.ExitCode,
            pack.StandardOutput,
            pack.StandardError));

        var packagePath = Path.Combine(feedDirectory, $"{PackageId}.{packageVersion}.nupkg");
        scenarios.Add(Scenario(
            "nupkg-contract",
            "The generated SDK package has the expected id, library asset and readme.",
            ValidatePackage(packagePath, packageVersion),
            outputDirectory));

        var consumerProjectPath = WriteConsumer(consumerDirectory, feedDirectory, packageVersion);
        var restore = RunDotnet(consumerDirectory, "restore", consumerProjectPath, "--nologo");
        scenarios.Add(Scenario(
            "external-consumer-restore",
            "A separate consumer restores the SDK from the local package feed as a PackageReference.",
            DiagnosticsFromProcess("BI26020", "dotnet restore external consumer", restore),
            outputDirectory,
            restore.ExitCode,
            restore.StandardOutput,
            restore.StandardError));

        var build = RunDotnet(consumerDirectory, "build", consumerProjectPath, "--no-restore", "--nologo", "--verbosity", "quiet", "/p:WarningLevel=0");
        scenarios.Add(Scenario(
            "external-consumer-build",
            "A separate consumer builds against the packaged SDK without WinForms references.",
            DiagnosticsFromProcess("BI26030", "dotnet build external consumer", build),
            outputDirectory,
            build.ExitCode,
            build.StandardOutput,
            build.StandardError));

        // Run the consumer's built assembly directly rather than via `dotnet run`. `dotnet run`
        // re-evaluates the project through MSBuild even with --no-build, which spawns a full set of
        // worker nodes that outlive the call, and it will not accept -nodeReuse:false to stop them
        // ("Unknown command", exit 2). Invoking the DLL skips MSBuild entirely -- no nodes, and a
        // faster call -- while exercising exactly the same packaged-SDK code path.
        var consumerAssembly = FindConsumerAssembly(consumerDirectory, consumerProjectPath);
        var validate = RunDotnet(consumerDirectory, consumerAssembly, "validate", projectPath);
        scenarios.Add(Scenario(
            "external-consumer-validate",
            "The packaged SDK validates a real installer project from a separate app.",
            DiagnosticsFromProcess("BI26040", "external consumer validate", validate),
            outputDirectory,
            validate.ExitCode,
            validate.StandardOutput,
            validate.StandardError));

        var plan = RunDotnet(consumerDirectory, consumerAssembly, "plan", projectPath);
        scenarios.Add(Scenario(
            "external-consumer-plan",
            "The packaged SDK compiles a deterministic plan from a separate app.",
            DiagnosticsFromProcess("BI26050", "external consumer plan", plan),
            outputDirectory,
            plan.ExitCode,
            plan.StandardOutput,
            plan.StandardError));

        var leakDiagnostics = ValidateNoSecretLeak(outputDirectory);
        scenarios.Add(Scenario(
            "no-sdk-secret-leak",
            "Generated SDK qualification evidence does not leak seeded secret literals.",
            leakDiagnostics,
            outputDirectory));

        return Complete(projectPath, outputDirectory, sdkProjectPath, packageVersion, packagePath, consumerProjectPath, directPlan.PlanHash, started, scenarios);
    }

    public static void WriteReport(HeadlessSdkQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, HeadlessSdkQualificationJsonContext.Default.HeadlessSdkQualificationReport));
    }

    private static List<ProjectSchemaDiagnostic> ValidateSdkProjectMetadata(string sdkProjectPath)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!File.Exists(sdkProjectPath))
        {
            diagnostics.Add(Error("BI26002", "SdkProjectPath", $"SDK project file does not exist: {sdkProjectPath}"));
            return diagnostics;
        }

        var doc = XDocument.Load(sdkProjectPath);
        var values = doc.Descendants()
            .Where(e => e.Name.LocalName is "PackageId" or "PackageReadmeFile" or "Title" or "PackageTags")
            .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("PackageId", out var packageId) || !string.Equals(packageId, PackageId, StringComparison.Ordinal))
            diagnostics.Add(Error("BI26003", "SdkProject.PackageId", $"SDK PackageId must be '{PackageId}'."));
        if (!values.TryGetValue("PackageReadmeFile", out var readme) || string.IsNullOrWhiteSpace(readme))
            diagnostics.Add(Error("BI26004", "SdkProject.PackageReadmeFile", "SDK package must declare a package readme."));
        if (!values.TryGetValue("Title", out var title) || !title.Contains("Headless SDK", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI26005", "SdkProject.Title", "SDK package title must clearly identify the headless SDK."));
        if (!values.TryGetValue("PackageTags", out var tags) || !tags.Contains("installer", StringComparison.OrdinalIgnoreCase) || !tags.Contains("ci", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI26006", "SdkProject.PackageTags", "SDK package tags must cover installer and CI usage."));

        return diagnostics;
    }

    private static List<ProjectSchemaDiagnostic> ValidatePackage(string packagePath, string packageVersion)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!File.Exists(packagePath))
        {
            diagnostics.Add(Error("BI26011", "Package", $"SDK package was not produced: {packagePath}"));
            return diagnostics;
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        if (!entries.Contains("lib/net10.0/Beep.Installer.Core.dll", StringComparer.Ordinal))
            diagnostics.Add(Error("BI26012", "Package.lib", "SDK package is missing the Core library asset."));
        if (!entries.Contains("lib/net10.0/DataManagementEngine.dll", StringComparer.Ordinal)
            || !entries.Contains("lib/net10.0/DataManagementModels.dll", StringComparer.Ordinal))
            diagnostics.Add(Error("BI26018", "Package.lib", "SDK package must include the BeepDM source assembly outputs it was compiled against."));
        if (!entries.Contains("README.md", StringComparer.Ordinal))
            diagnostics.Add(Error("BI26013", "Package.readme", "SDK package is missing README.md."));

        var nuspec = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
        {
            diagnostics.Add(Error("BI26014", "Package.nuspec", "SDK package is missing nuspec metadata."));
            return diagnostics;
        }

        using var stream = nuspec.Open();
        var doc = XDocument.Load(stream);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = doc.Root?.Element(ns + "metadata");
        var id = metadata?.Element(ns + "id")?.Value ?? "";
        var version = metadata?.Element(ns + "version")?.Value ?? "";
        if (!string.Equals(id, PackageId, StringComparison.Ordinal))
            diagnostics.Add(Error("BI26015", "Package.id", $"SDK package id was '{id}', expected '{PackageId}'."));
        if (!string.Equals(version, packageVersion, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI26016", "Package.version", $"SDK package version was '{version}', expected '{packageVersion}'."));
        if (metadata?.Element(ns + "readme") is null)
            diagnostics.Add(Error("BI26017", "Package.readme", "SDK package nuspec does not declare a readme."));

        return diagnostics;
    }

    private static string WriteConsumer(string consumerDirectory, string feedDirectory, string packageVersion)
    {
        var projectPath = Path.Combine(consumerDirectory, "ExternalHeadlessSdkConsumer.csproj");
        File.WriteAllText(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>packages</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{{PackageId}}" Version="{{packageVersion}}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(consumerDirectory, "NuGet.config"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local-sdk" value="{{SecurityElementEscape(feedDirectory)}}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(consumerDirectory, "Program.cs"), """
            using Beep.Installer.Engine;

            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: external-consumer validate|plan <project.bsetup>");
                return 2;
            }

            var command = args[0].Trim().ToLowerInvariant();
            var projectPath = args[1];

            if (command == "validate")
            {
                var result = HeadlessInstallerSdk.Validate(new HeadlessInstallerRequest
                {
                    ProjectPath = projectPath,
                    Strict = true
                });
                foreach (var diagnostic in result.Diagnostics)
                    Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
                Console.WriteLine(result.Success ? $"Validated {result.ProductName} {result.ProductVersion}" : "Validation failed.");
                return result.ExitCode;
            }

            if (command == "plan")
            {
                var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
                {
                    ProjectPath = projectPath,
                    Strict = true
                });
                foreach (var diagnostic in result.Diagnostics)
                    Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
                if (result.Success)
                    Console.WriteLine($"Plan hash: {result.PlanHash}");
                return result.ExitCode;
            }

            Console.Error.WriteLine($"Unknown command: {command}");
            return 2;
            """);
        return projectPath;
    }

    private static List<ProjectSchemaDiagnostic> DiagnosticsFromProcess(string code, string path, ProcessRunResult result)
    {
        if (result.ExitCode == 0)
            return new List<ProjectSchemaDiagnostic>();

        return new List<ProjectSchemaDiagnostic>
        {
            Error(code, path, $"{path} failed with exit code {result.ExitCode}: {FirstNonEmptyLine(result.StandardError, result.StandardOutput)}")
        };
    }

    private static List<ProjectSchemaDiagnostic> ValidateNoSecretLeak(string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("hunter2", StringComparison.Ordinal)
                || text.Contains("abc123", StringComparison.Ordinal)
                || text.Contains("super-secret", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI26060", Path.GetFileName(file), "SDK qualification evidence leaked a seeded secret."));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// How long any single <c>dotnet</c> invocation in this qualification may take. A pack or a
    /// restore on a cold NuGet cache is genuinely slow, so this is deliberately generous -- it is a
    /// backstop against a wedge, not a performance budget.
    /// </summary>
    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Verbs that drive MSBuild and therefore accept (and need) <c>-nodeReuse:false</c>.</summary>
    private static readonly HashSet<string> MsBuildDrivenVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "build", "pack", "restore", "publish", "msbuild" };

    private static ProcessRunResult RunDotnet(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // MSBuild keeps worker nodes alive for reuse after the build finishes, and those nodes
        // inherit the redirected stdout/stderr handles. The pipe then never reaches EOF even though
        // `dotnet` itself has exited, so reading it to the end waits on processes that are under no
        // obligation to leave. That is not hypothetical: it hung the whole test suite indefinitely,
        // with `dotnet` gone and orphaned MSBuild nodes holding the write end.
        //
        // The environment variable alone does not do it. The .NET CLI passes an explicit
        // /nodeReuse:true when it invokes MSBuild, and an explicit switch wins -- measured, a full
        // node set survives the build regardless of the variable. The switch is added below, for
        // the MSBuild-driven verbs only: `dotnet run` would treat it as an argument to the app.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // Only the MSBuild-driven verbs. `dotnet run` rejects the switch outright -- it reports
        // "Unknown command: -nodereuse:false" and exits 2 -- so the consumer is invoked through its
        // built DLL instead of `dotnet run`, which avoids MSBuild (and its nodes) altogether.
        if (arguments.Length > 0 && MsBuildDrivenVerbs.Contains(arguments[0]))
            start.ArgumentList.Add("-nodeReuse:false");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");

        // Read both pipes concurrently: a child that fills one while nothing drains the other
        // deadlocks against a full pipe buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var deadline = DateTime.UtcNow + DotnetTimeout;
        if (!process.WaitForExit((int)DotnetTimeout.TotalMilliseconds))
        {
            KillTree(process);
            return new ProcessRunResult(
                -1,
                Drained(stdoutTask),
                $"'dotnet {string.Join(' ', arguments)}' did not exit within {DotnetTimeout.TotalMinutes:0} minutes and was terminated.");
        }

        // Belt and braces: even with node reuse disabled, anything else the build spawned could hold
        // the handle. Bound the drain rather than letting it wait forever.
        var remaining = deadline - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, remaining))
        {
            return new ProcessRunResult(
                process.ExitCode,
                Drained(stdoutTask),
                Drained(stderrTask) +
                $"{Environment.NewLine}'dotnet {string.Join(' ', arguments)}' exited but its output pipes stayed open; " +
                "a child process is still holding them.");
        }

        return new ProcessRunResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <summary>
    /// The consumer's built entry assembly. The build above uses the default configuration, but the
    /// path is searched rather than assumed so a different TFM or configuration does not silently
    /// fall back to "no output" and fail the scenario for the wrong reason.
    /// </summary>
    private static string FindConsumerAssembly(string consumerDirectory, string consumerProjectPath)
    {
        var name = Path.GetFileNameWithoutExtension(consumerProjectPath) + ".dll";
        var bin = Path.Combine(consumerDirectory, "bin");
        if (Directory.Exists(bin))
        {
            var match = Directory.EnumerateFiles(bin, name, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (match != null) return match;
        }

        // Let the run fail with dotnet's own "file not found" rather than inventing a message.
        return Path.Combine(consumerDirectory, "bin", "Debug", name);
    }

    /// <summary>Whatever a read task produced, or a note that it never finished.</summary>
    private static string Drained(Task<string> read)
        => read.IsCompletedSuccessfully ? read.Result : "";

    private static void KillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already gone, or not ours to kill */ }
    }

    private static HeadlessSdkQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string sdkProjectPath,
        string packageVersion,
        string packagePath,
        string consumerProjectPath,
        string planHash,
        DateTimeOffset started,
        List<HeadlessSdkQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new HeadlessSdkQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            SdkProjectPath = sdkProjectPath,
            PackageId = HeadlessSdkQualificationRunner.PackageId,
            PackageVersion = packageVersion,
            PackagePath = packagePath,
            ConsumerProjectPath = consumerProjectPath,
            PlanHash = planHash,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Headless SDK package qualification completed." : "Headless SDK package qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static HeadlessSdkQualificationScenario Scenario(
        string id,
        string description,
        IEnumerable<ProjectSchemaDiagnostic> diagnostics,
        string outputDirectory,
        int exitCode = 0,
        string standardOutput = "",
        string standardError = "")
    {
        var items = diagnostics.ToList();
        var success = exitCode == 0 && items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".headless-sdk.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            id,
            exitCode,
            diagnostics = items,
            standardOutput = Redact(standardOutput),
            standardError = Redact(standardError)
        }, EvidenceJsonOptions));
        return new HeadlessSdkQualificationScenario
        {
            Id = id,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : exitCode == 0 ? 1 : exitCode,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items,
            StandardOutput = Redact(standardOutput),
            StandardError = Redact(standardError)
        };
    }

    private static string FindSdkProjectPath()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Beep.Installer.Core", "Beep.Installer.Core.csproj");
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate Beep.Installer.Core.csproj. Pass /SDKPROJECT=<path>.");
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static string FirstNonEmptyLine(params string[] values)
        => values.SelectMany(value => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "No process output was captured.";

    private static string Redact(string value)
        => value.Replace("hunter2", "[redacted]", StringComparison.Ordinal)
            .Replace("abc123", "[redacted]", StringComparison.Ordinal)
            .Replace("super-secret", "[redacted]", StringComparison.Ordinal);

    private static string SecurityElementEscape(string value)
        => System.Security.SecurityElement.Escape(value) ?? value;

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(HeadlessSdkQualificationReport))]
[JsonSerializable(typeof(HeadlessSdkQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class HeadlessSdkQualificationJsonContext : JsonSerializerContext;
