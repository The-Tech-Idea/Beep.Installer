using Beep.Installer.Engine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record WebDeployCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IWebDeployCommandRunner
{
    bool IsSupported { get; }
    WebDeployCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class WebDeployPackageResourceProvider : IResourceProvider
{
    private readonly IWebDeployCommandRunner _runner;

    public WebDeployPackageResourceProvider()
        : this(new MsDeployCommandRunner())
    {
    }

    public WebDeployPackageResourceProvider(IWebDeployCommandRunner runner)
    {
        _runner = runner;
    }

    public string ResourceType => "webdeploy.package";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var packagePath = ResolveValue(Input(operation, "packagePath"), context);
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = Input(operation, "name"),
            ["packagePath"] = packagePath,
            ["siteName"] = Input(operation, "siteName"),
            ["msdeploy"] = _runner.IsSupported ? "available" : "missing"
        };

        return new ResourceDetectionResult
        {
            Exists = File.Exists(packagePath),
            Facts = facts
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5601", $"{operation.Id}.name", "Web Deploy package operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "packagePath")))
            return Error("BI5602", $"{operation.Id}.packagePath", "Web Deploy package operation is missing packagePath.");
        if (string.IsNullOrWhiteSpace(Input(operation, "siteName")))
            return Error("BI5603", $"{operation.Id}.siteName", "Web Deploy package operation is missing siteName.");
        if (!context.DryRun && !File.Exists(ResolveValue(Input(operation, "packagePath"), context)))
            return Error("BI5604", $"{operation.Id}.packagePath", $"Web Deploy package file does not exist: {ResolveValue(Input(operation, "packagePath"), context)}");
        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI5605", operation.Id, "Web Deploy package operations require msdeploy.exe.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
        => new()
        {
            ChangeKind = ResourceChangeKind.Update,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: Web Deploy package would be synced with msdeploy.exe." };

        var run = _runner.Run(BuildSyncArguments(operation, context));
        if (run.ExitCode != 0)
            return CommandFailure("BI5610", operation.Id, run, "sync Web Deploy package");

        return new ResourceProviderResult
        {
            Message = "Web Deploy package synced through msdeploy.exe.",
            Evidence =
            {
                ["packagePath"] = ResolveValue(Input(operation, "packagePath"), context),
                ["siteName"] = Input(operation, "siteName"),
                ["destination"] = Destination(operation)
            }
        };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: Web Deploy package rollback would delete the target IIS app when RemoveOnUninstall is true." };
        if (!BoolInput(operation, "removeOnUninstall", defaultValue: true))
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Web Deploy rollback skipped because removeOnUninstall is false." };

        var run = _runner.Run(new[]
        {
            "-verb:delete",
            $"-dest:iisApp='{Input(operation, "siteName")}'"
        });
        if (run.ExitCode != 0)
            return CommandFailure("BI5620", operation.Id, run, "delete Web Deploy target");

        return new ResourceProviderResult { Message = "Web Deploy target deleted through msdeploy.exe." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        return new ResourceProviderResult
        {
            Message = "Web Deploy package inputs are valid.",
            Evidence =
            {
                ["packagePath"] = ResolveValue(Input(operation, "packagePath"), context),
                ["siteName"] = Input(operation, "siteName")
            }
        };
    }

    private static List<string> BuildSyncArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = new List<string>
        {
            "-verb:sync",
            $"-source:package='{ResolveValue(Input(operation, "packagePath"), context)}'",
            $"-dest:{Destination(operation)}",
            $"-setParam:name='IIS Web Application Name',value='{Input(operation, "siteName")}'"
        };

        foreach (var parameter in Parameters(operation))
            arguments.Add($"-setParam:name='{parameter.Key}',value='{parameter.Value}'");

        return arguments;
    }

    private static IEnumerable<KeyValuePair<string, string>> Parameters(CompiledInstallOperation operation)
    {
        var count = IntInput(operation, "parameterCount");
        for (var i = 0; i < count; i++)
        {
            var name = Input(operation, $"parameter.{i}.name");
            if (!string.IsNullOrWhiteSpace(name))
                yield return new KeyValuePair<string, string>(name, Input(operation, $"parameter.{i}.value"));
        }
    }

    private static string Destination(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "destination")) ? "auto" : Input(operation, "destination");

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static int IntInput(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value)
           && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string ResolveValue(string value, ResourceProviderContext context)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var resolved = value;
        if (!string.IsNullOrWhiteSpace(context.InstallRoot))
            resolved = resolved.Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            resolved = resolved.Replace($"%{variable.Key}%", variable.Value, StringComparison.OrdinalIgnoreCase);
            resolved = resolved.Replace($"{{{variable.Key}}}", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static ResourceProviderResult Error(string code, string path, string message)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Diagnostics = new List<ProjectSchemaDiagnostic>
            {
                new(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };

    private static ResourceProviderResult CommandFailure(string code, string path, WebDeployCommandResult result, string verb)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        var message = $"Failed to {verb}. msdeploy.exe exited with code {result.ExitCode}.";
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail.Trim()}";
        return Error(code, path, message);
    }
}

internal sealed class MsDeployCommandRunner : IWebDeployCommandRunner
{
    private static readonly string ProgramFilesMsDeploy = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "IIS",
        "Microsoft Web Deploy V3",
        "msdeploy.exe");

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(ProgramFilesMsDeploy);

    public WebDeployCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new WebDeployCommandResult(-1, "", "msdeploy.exe is not available. Install Microsoft Web Deploy.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ProgramFilesMsDeploy,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new WebDeployCommandResult(process.ExitCode, standardOutput, standardError);
    }
}
