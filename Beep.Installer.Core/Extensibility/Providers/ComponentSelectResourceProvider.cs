using Beep.Installer.Engine;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Extensibility.Providers;

/// <summary>
/// Provider for component selection gates.
///
/// It does not mutate the machine. Its job is to turn authored component selection into
/// executable graph state so dependent operations are skipped deterministically when an
/// optional component is not selected.
/// </summary>
public sealed class ComponentSelectResourceProvider : IResourceProvider
{
    public string ResourceType => "component.select";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
        => new()
        {
            Exists = IsSelected(operation, context),
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["required"] = Required(operation).ToString(),
                ["selected"] = IsSelected(operation, context).ToString(),
                ["condition"] = operation.Condition
            }
        };

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "id")))
            return Error("BI3401", $"{operation.Id}.id", "Component selection operation is missing id.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = IsSelected(operation, context) ? ResourceChangeKind.Create : ResourceChangeKind.None,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
        => IsSelected(operation, context)
            ? new ResourceProviderResult { Message = $"Component '{Input(operation, "id")}' selected and applicable." }
            : new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Component '{Input(operation, "id")}' is not selected or not applicable."
            };

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
        => new() { Message = $"Component '{Input(operation, "id")}' selection gate verified." };

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
        => new()
        {
            Code = ResourceProviderResultCode.Skipped,
            Message = "Component selection gate has no rollback action."
        };

    private static bool IsSelected(CompiledInstallOperation operation, ResourceProviderContext context)
        => ConditionsMet(operation, context) && (Required(operation) || BoolInput(operation, "selected", defaultValue: true));

    private static bool Required(CompiledInstallOperation operation)
        => BoolInput(operation, "required", defaultValue: false);

    private static bool ConditionsMet(CompiledInstallOperation operation, ResourceProviderContext context)
        => InstallerConditionFactsEvaluator.EvaluateCompiledOperation(operation, context);

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static ResourceProviderResult Error(string code, string path, string message)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Diagnostics =
            {
                new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };
}

public sealed class SystemInstallerConditionFacts : IInstallerConditionFacts
{
    public static SystemInstallerConditionFacts Instance { get; } = new();

    private SystemInstallerConditionFacts()
    {
    }

    public Version OsVersion => Environment.OSVersion.Version;
    public string Architecture => RuntimeInformation.OSArchitecture.ToString();

    public bool IsAdmin
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool FileExists(string path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(Environment.ExpandEnvironmentVariables(path));

    public bool DirectoryExists(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(Environment.ExpandEnvironmentVariables(path));

    public bool RegistryKeyExists(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || !OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public string? RegistryValue(string keyPath, string valueName)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || !OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public CommandConditionResult RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new CommandConditionResult(-1, "");

        try
        {
            var parts = command.Split(' ', 2);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = parts[0],
                    Arguments = parts.Length > 1 ? parts[1] : "",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10000);
            return new CommandConditionResult(process.ExitCode, output);
        }
        catch
        {
            return new CommandConditionResult(-1, "");
        }
    }
}
