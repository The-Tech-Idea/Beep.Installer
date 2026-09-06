using Beep.Installer.Engine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record DriverCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IDriverCommandRunner
{
    bool IsSupported { get; }
    DriverCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class DriverPackageResourceProvider : IResourceProvider
{
    private readonly IDriverCommandRunner _runner;
    private readonly Dictionary<string, string> _publishedNames = new(StringComparer.OrdinalIgnoreCase);

    public DriverPackageResourceProvider()
        : this(new PnPUtilDriverCommandRunner())
    {
    }

    public DriverPackageResourceProvider(IDriverCommandRunner runner)
    {
        _runner = runner;
    }

    public string ResourceType => "driver.package";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["infPath"] = ResolveValue(Input(operation, "infPath"), context),
            ["publishedName"] = PublishedName(operation)
        };

        if (context.DryRun)
        {
            facts["mode"] = "dry-run";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        if (!_runner.IsSupported)
        {
            facts["platform"] = "unsupported";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        var query = _runner.Run(new[] { "/enum-drivers" });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);
        var exists = query.ExitCode == 0 && MatchesDriver(operation, query.StandardOutput);
        if (exists && string.IsNullOrWhiteSpace(facts["publishedName"]))
        {
            var parsed = ParsePublishedNameForOperation(operation, query.StandardOutput);
            if (!string.IsNullOrWhiteSpace(parsed))
                facts["publishedName"] = parsed;
        }

        return new ResourceDetectionResult
        {
            Exists = exists,
            Facts = facts
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!IsPnpDriverPackage(operation))
            return Error("BI5104", $"{operation.Id}.kind", "Runtime driver.package execution supports PnP INF packages; kernel and file-system drivers are authored through MSI package export.");

        var infPath = ResolveValue(Input(operation, "infPath"), context);
        if (string.IsNullOrWhiteSpace(infPath))
            return Error("BI5101", $"{operation.Id}.infPath", "Driver package operation is missing infPath.");
        if (!infPath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            return Error("BI5102", $"{operation.Id}.infPath", $"Driver package '{infPath}' must target an .inf file.");
        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI5103", operation.Id, "Driver package operations require Windows pnputil support.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.None : ResourceChangeKind.Create,
            Operations = detection.Exists ? new List<CompiledInstallOperation>() : new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: driver package would be staged with pnputil."
            };

        if (Detect(operation, context).Exists)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Driver package is already staged."
            };

        var arguments = new List<string> { "/add-driver", ResolveValue(Input(operation, "infPath"), context) };
        if (BoolInput(operation, "installDevices"))
            arguments.Add("/install");

        var result = _runner.Run(arguments);
        if (result.ExitCode != 0)
            return CommandFailure("BI5110", operation.Id, result, "stage driver package");

        var publishedName = ParsePublishedName(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(publishedName))
            _publishedNames[operation.Id] = publishedName;

        return new ResourceProviderResult
        {
            Message = string.IsNullOrWhiteSpace(publishedName)
                ? "Driver package staged with pnputil."
                : $"Driver package staged with pnputil as '{publishedName}'."
        };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: driver package rollback would delete the published driver."
            };

        var publishedName = ResolvePublishedName(operation, context);
        if (string.IsNullOrWhiteSpace(publishedName))
            return Error("BI5120", operation.Id, "Driver package rollback requires a published oem*.inf name.");

        var result = _runner.Run(new[] { "/delete-driver", publishedName, "/uninstall", "/force" });
        if (result.ExitCode != 0)
            return CommandFailure("BI5121", operation.Id, result, "delete driver package");

        return new ResourceProviderResult
        {
            Message = $"Driver package '{publishedName}' deleted with pnputil."
        };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: driver package verification would enumerate staged drivers."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var detection = Detect(operation, context);
        if (!detection.Exists)
            return Error("BI5130", operation.Id, "Driver package verification failed because the package is not staged.");

        return new ResourceProviderResult
        {
            Message = "Driver package is staged in the Windows driver store."
        };
    }

    private string ResolvePublishedName(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var authored = PublishedName(operation);
        if (!string.IsNullOrWhiteSpace(authored))
            return authored;

        if (_publishedNames.TryGetValue(operation.Id, out var captured) && !string.IsNullOrWhiteSpace(captured))
            return captured;

        var query = _runner.Run(new[] { "/enum-drivers" });
        return query.ExitCode == 0 ? ParsePublishedNameForOperation(operation, query.StandardOutput) : "";
    }

    private static bool MatchesDriver(CompiledInstallOperation operation, string output)
    {
        var publishedName = PublishedName(operation);
        if (!string.IsNullOrWhiteSpace(publishedName)
            && output.Contains(publishedName, StringComparison.OrdinalIgnoreCase))
            return true;

        var originalName = Path.GetFileName(Input(operation, "infPath"));
        if (!string.IsNullOrWhiteSpace(originalName)
            && output.Contains(originalName, StringComparison.OrdinalIgnoreCase))
            return true;

        var className = Input(operation, "className");
        return !string.IsNullOrWhiteSpace(className)
               && output.Contains(className, StringComparison.OrdinalIgnoreCase);
    }

    private static string ParsePublishedNameForOperation(CompiledInstallOperation operation, string output)
    {
        var blocks = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            if (MatchesDriver(operation, block))
                return ParsePublishedName(block);
        }

        return "";
    }

    private static string ParsePublishedName(string output)
    {
        foreach (var line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon < 0)
                continue;

            var key = trimmed[..colon].Trim();
            if (!key.Equals("Published Name", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(colon + 1)..].Trim();
            if (value.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return "";
    }

    private static string PublishedName(CompiledInstallOperation operation)
        => Input(operation, "publishedName");

    private static bool BoolInput(CompiledInstallOperation operation, string key)
        => bool.TryParse(Input(operation, key), out var value) && value;

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

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool IsPnpDriverPackage(CompiledInstallOperation operation)
    {
        var kind = Input(operation, "kind");
        return string.IsNullOrWhiteSpace(kind) || kind.Equals("pnp", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

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

    private static ResourceProviderResult CommandFailure(
        string code,
        string path,
        DriverCommandResult result,
        string verb)
    {
        var message = $"Failed to {verb}. pnputil exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";

        return Error(code, path, message);
    }
}

internal sealed class PnPUtilDriverCommandRunner : IDriverCommandRunner
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public DriverCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new DriverCommandResult(-1, "", "pnputil is only available on Windows.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
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

        return new DriverCommandResult(process.ExitCode, standardOutput, standardError);
    }
}
