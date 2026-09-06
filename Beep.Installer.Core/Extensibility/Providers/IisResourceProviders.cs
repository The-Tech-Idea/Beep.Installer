using Beep.Installer.Engine;
using Beep.Installer.Policy;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record IisCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IIisCommandRunner
{
    bool IsSupported { get; }
    IisCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed record IisFeatureEnablementResult(bool Success, string Message);

public interface IIisFeatureManager
{
    IisFeatureEnablementResult EnableFeatures(IReadOnlyList<string> featureNames);
}

public sealed class IisAppPoolResourceProvider : IResourceProvider
{
    private readonly IIisCommandRunner _runner;
    private readonly IIisFeatureManager _featureManager;
    private static readonly string[] RequiredFeatures =
    {
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-ApplicationDevelopment",
        "IIS-NetFxExtensibility45",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-ManagementScriptingTools"
    };

    public IisAppPoolResourceProvider()
        : this(new AppCmdIisCommandRunner())
    {
    }

    public IisAppPoolResourceProvider(IIisCommandRunner runner, IIisFeatureManager? featureManager = null)
    {
        _runner = runner;
        _featureManager = featureManager ?? new DismIisFeatureManager();
    }

    public string ResourceType => "iis.appPool";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope | InstallerExtensionPermission.Secrets;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = Input(operation, "name")
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

        var query = _runner.Run(new[] { "list", "apppool", $"/name:{Input(operation, "name")}" });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);
        return new ResourceDetectionResult { Exists = query.ExitCode == 0 && !IsNotFound(query), Facts = facts };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5401", $"{operation.Id}.name", "IIS application pool operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "identity")))
            return Error("BI5402", $"{operation.Id}.identity", "IIS application pool operation is missing identity.");
        if (Input(operation, "identity").Equals("SpecificUser", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(Input(operation, "username")))
            return Error("BI5403", $"{operation.Id}.username", "IIS application pool identity SpecificUser requires username.");

        var password = Input(operation, "password");
        if (Input(operation, "identity").Equals("SpecificUser", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(password)
            && password != "<redacted>"
            && !SecretReference.IsReference(password))
            return Error("BI5405", $"{operation.Id}.password", "IIS application pool SpecificUser password must be an opaque secret reference such as env:NAME or secret://env/NAME.");

        if (SecretReference.IsReference(password)
            && !SecretReference.TryParse(password, out _, out var parseError))
            return Error("BI5406", $"{operation.Id}.password", $"IIS application pool password secret reference is invalid: {parseError}");

        if (!context.DryRun && !_runner.IsSupported && !CanEnableRequiredFeatures(context, RequiredFeatures, out var policyError))
            return Error("BI5404", operation.Id, policyError);

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS application pool would be created or updated." };

        var support = EnsureIisSupport(context, RequiredFeatures, "BI5431", operation.Id);
        if (support.Code == ResourceProviderResultCode.Failed)
            return support;

        var exists = Detect(operation, context).Exists;
        if (!exists)
        {
            var add = _runner.Run(new[] { "add", "apppool", $"/name:{Input(operation, "name")}" });
            if (add.ExitCode != 0)
                return CommandFailure("BI5410", operation.Id, add, "create IIS application pool");
        }

        var passwordResolution = ResolvePassword(operation, context);
        if (passwordResolution.Result.Code == ResourceProviderResultCode.Failed)
            return passwordResolution.Result;

        var set = _runner.Run(BuildSetArguments(operation, passwordResolution.Password));
        if (set.ExitCode != 0)
            return CommandFailure("BI5411", operation.Id, set, "configure IIS application pool");

        if (BoolInput(operation, "startAfterInstall", defaultValue: true))
        {
            var start = _runner.Run(new[] { "start", "apppool", $"/apppool.name:{Input(operation, "name")}" });
            if (start.ExitCode != 0 && !start.StandardError.Contains("already", StringComparison.OrdinalIgnoreCase))
                return CommandFailure("BI5412", operation.Id, start, "start IIS application pool");
        }

        return new ResourceProviderResult
        {
            Message = exists
                ? "IIS application pool updated through appcmd."
                : "IIS application pool created through appcmd."
        };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS application pool rollback would delete the pool." };

        if (!Detect(operation, context).Exists)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "IIS application pool rollback skipped because the pool does not exist." };

        _runner.Run(new[] { "stop", "apppool", $"/apppool.name:{Input(operation, "name")}" });
        var delete = _runner.Run(new[] { "delete", "apppool", $"/apppool.name:{Input(operation, "name")}" });
        if (delete.ExitCode != 0)
            return CommandFailure("BI5420", operation.Id, delete, "delete IIS application pool");

        return new ResourceProviderResult { Message = "IIS application pool deleted through appcmd." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS application pool verification would query appcmd." };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        return Detect(operation, context).Exists
            ? new ResourceProviderResult { Message = "IIS application pool exists." }
            : Error("BI5430", operation.Id, "IIS application pool verification failed because the pool does not exist.");
    }

    private static List<string> BuildSetArguments(CompiledInstallOperation operation, string password)
    {
        var arguments = new List<string>
        {
            "set",
            "apppool",
            $"/apppool.name:{Input(operation, "name")}",
            $"/managedRuntimeVersion:{Input(operation, "runtimeVersion")}",
            $"/managedPipelineMode:{PipelineMode(operation)}",
            $"/enable32BitAppOnWin64:{BoolText(operation, "enable32Bit")}",
            $"/autoStart:{BoolText(operation, "autoStart", defaultValue: true)}",
            $"/processModel.identityType:{Input(operation, "identity")}"
        };

        if (Input(operation, "identity").Equals("SpecificUser", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add($"/processModel.userName:{Input(operation, "username")}");
            if (!string.IsNullOrWhiteSpace(password))
                arguments.Add($"/processModel.password:{password}");
        }

        return arguments;
    }

    private static (string Password, ResourceProviderResult Result) ResolvePassword(
        CompiledInstallOperation operation,
        ResourceProviderContext context)
    {
        var value = Input(operation, "password");
        if (string.IsNullOrWhiteSpace(value) || value == "<redacted>")
            return ("", new ResourceProviderResult());

        if (!SecretReference.TryParse(value, out var reference, out var parseError))
            return ("", Error("BI5406", $"{operation.Id}.password", $"IIS application pool password secret reference is invalid: {parseError}"));

        var resolved = context.SecretProvider.Resolve(reference);
        if (!resolved.Success || resolved.Value is null)
            return ("", Error("BI5407", $"{operation.Id}.password", resolved.Error ?? $"IIS application pool password secret reference '{value}' could not be resolved."));

        return (resolved.Value, new ResourceProviderResult());
    }

    private static string PipelineMode(CompiledInstallOperation operation)
        => Input(operation, "pipelineMode").Equals("classic", StringComparison.OrdinalIgnoreCase) ? "Classic" : "Integrated";

    private static string BoolText(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => BoolInput(operation, key, defaultValue) ? "true" : "false";

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool IsNotFound(IisCommandResult result)
        => result.StandardOutput.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("Cannot find", StringComparison.OrdinalIgnoreCase);

    private ResourceProviderResult EnsureIisSupport(
        ResourceProviderContext context,
        IReadOnlyList<string> requiredFeatures,
        string errorCode,
        string path)
    {
        if (_runner.IsSupported)
            return new ResourceProviderResult();

        if (!CanEnableRequiredFeatures(context, requiredFeatures, out var policyError))
            return Error(errorCode, path, policyError);

        var enablement = _featureManager.EnableFeatures(requiredFeatures);
        if (!enablement.Success)
            return Error(errorCode, path, enablement.Message);

        return _runner.IsSupported
            ? new ResourceProviderResult
            {
                Evidence =
                {
                    ["iisFeatureEnablement"] = "enabled",
                    ["iisFeatures"] = string.Join(",", requiredFeatures)
                }
            }
            : Error(errorCode, path, "IIS feature enablement completed but appcmd.exe is still unavailable.");
    }

    private static bool CanEnableRequiredFeatures(
        ResourceProviderContext context,
        IReadOnlyList<string> requiredFeatures,
        out string error)
    {
        var policy = context.Policy;
        if (policy?.AllowIisFeatureEnablement != true)
        {
            error = "IIS operations require Windows IIS appcmd support. Set policy AllowIisFeatureEnablement=true to let the installer enable required IIS Windows features.";
            return false;
        }

        if (policy.AllowedIisFeatures.Count > 0)
        {
            var denied = requiredFeatures
                .Where(feature => !policy.AllowedIisFeatures.Contains(feature, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (denied.Count > 0)
            {
                error = $"IIS feature enablement is blocked by policy. Add these required IIS features to AllowedIisFeatures: {string.Join(", ", denied)}.";
                return false;
            }
        }

        error = "";
        return true;
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

    private static ResourceProviderResult CommandFailure(string code, string path, IisCommandResult result, string verb)
    {
        var message = $"Failed to {verb}. appcmd.exe exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";
        return Error(code, path, message);
    }
}

public sealed class IisSiteResourceProvider : IResourceProvider
{
    private readonly IIisCommandRunner _runner;
    private readonly IIisFeatureManager _featureManager;
    private static readonly string[] RequiredFeatures =
    {
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-StaticContent",
        "IIS-ManagementScriptingTools"
    };

    public IisSiteResourceProvider()
        : this(new AppCmdIisCommandRunner())
    {
    }

    public IisSiteResourceProvider(IIisCommandRunner runner, IIisFeatureManager? featureManager = null)
    {
        _runner = runner;
        _featureManager = featureManager ?? new DismIisFeatureManager();
    }

    public string ResourceType => "iis.site";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = Input(operation, "name"),
            ["physicalPath"] = ResolveValue(Input(operation, "physicalPath"), context)
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

        var query = _runner.Run(new[] { "list", "site", $"/name:{Input(operation, "name")}" });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);
        return new ResourceDetectionResult { Exists = query.ExitCode == 0 && !IsNotFound(query), Facts = facts };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5501", $"{operation.Id}.name", "IIS site operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "physicalPath")))
            return Error("BI5502", $"{operation.Id}.physicalPath", "IIS site operation is missing physicalPath.");

        foreach (var binding in Bindings(operation))
        {
            if (binding.Port is < 1 or > 65535)
                return Error("BI5503", $"{operation.Id}.binding.{binding.Index}.port", "IIS site binding port must be between 1 and 65535.");

            if (!binding.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
                && !binding.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                return Error("BI5506", $"{operation.Id}.binding.{binding.Index}.protocol", "IIS site binding protocol must be http or https.");

            if (!binding.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(binding.CertificateThumbprint))
                return Error("BI5507", $"{operation.Id}.binding.{binding.Index}.certificateThumbprint", "IIS HTTPS binding requires a certificate thumbprint.");
            if (!IsValidThumbprint(binding.CertificateThumbprint))
            {
                return Error("BI5504", $"{operation.Id}.binding.{binding.Index}.certificateThumbprint", "IIS HTTPS binding certificate thumbprint is invalid.");
            }
            if (!IsValidCertificateStoreName(binding.CertificateStoreName))
                return Error("BI5508", $"{operation.Id}.binding.{binding.Index}.certificateStoreName", "IIS HTTPS binding certificate store name must contain only letters, numbers, underscore or dash.");
            if (binding.SslFlags is < 0 or > 3)
                return Error("BI5509", $"{operation.Id}.binding.{binding.Index}.sslFlags", "IIS HTTPS binding sslFlags must be between 0 and 3.");
            if ((binding.SslFlags & 1) == 1 && string.IsNullOrWhiteSpace(binding.Host))
                return Error("BI5515", $"{operation.Id}.binding.{binding.Index}.host", "IIS HTTPS binding with SNI enabled requires a host name.");
        }

        if (!context.DryRun && !_runner.IsSupported && !CanEnableRequiredFeatures(context, RequiredFeatures, out var policyError))
            return Error("BI5505", operation.Id, policyError);

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS site would be created or updated." };

        var support = EnsureIisSupport(context, RequiredFeatures, "BI5531", operation.Id);
        if (support.Code == ResourceProviderResultCode.Failed)
            return support;

        var physicalPath = ResolveValue(Input(operation, "physicalPath"), context);
        Directory.CreateDirectory(physicalPath);

        var exists = Detect(operation, context).Exists;
        if (!exists)
        {
            var add = _runner.Run(new[] { "add", "site", $"/name:{Input(operation, "name")}", $"/physicalPath:{physicalPath}", $"/bindings:{FirstBindingText(operation)}" });
            if (add.ExitCode != 0)
                return CommandFailure("BI5510", operation.Id, add, "create IIS site");
        }
        else
        {
            var setVdir = _runner.Run(new[] { "set", "vdir", $"/vdir.name:{Input(operation, "name")}/", $"/physicalPath:{physicalPath}" });
            if (setVdir.ExitCode != 0)
                return CommandFailure("BI5511", operation.Id, setVdir, "update IIS site physical path");
        }

        if (!string.IsNullOrWhiteSpace(Input(operation, "applicationPool")))
        {
            var app = _runner.Run(new[] { "set", "app", $"/app.name:{Input(operation, "name")}/", $"/applicationPool:{Input(operation, "applicationPool")}" });
            if (app.ExitCode != 0)
                return CommandFailure("BI5512", operation.Id, app, "assign IIS application pool");
        }

        var bindings = Bindings(operation).ToList();
        for (var i = exists ? 0 : 1; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var addBinding = _runner.Run(new[] { "set", "site", $"/site.name:{Input(operation, "name")}", $"/+bindings.{BindingCollectionText(binding)}" });
            if (addBinding.ExitCode != 0 && !AlreadyExists(addBinding))
                return CommandFailure("BI5513", operation.Id, addBinding, "add IIS site binding");
        }

        if (BoolInput(operation, "startAfterInstall", defaultValue: true))
        {
            var start = _runner.Run(new[] { "start", "site", $"/site.name:{Input(operation, "name")}" });
            if (start.ExitCode != 0 && !start.StandardError.Contains("already", StringComparison.OrdinalIgnoreCase))
                return CommandFailure("BI5514", operation.Id, start, "start IIS site");
        }

        return new ResourceProviderResult
        {
            Message = exists
                ? "IIS site updated through appcmd."
                : "IIS site created through appcmd."
        };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS site rollback would delete the site." };

        if (!Detect(operation, context).Exists)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "IIS site rollback skipped because the site does not exist." };

        if (!BoolInput(operation, "removeOnUninstall", defaultValue: true))
        {
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "IIS site rollback skipped because removeOnUninstall is false; the site is treated as shared or externally owned."
            };
        }

        _runner.Run(new[] { "stop", "site", $"/site.name:{Input(operation, "name")}" });
        var delete = _runner.Run(new[] { "delete", "site", $"/site.name:{Input(operation, "name")}" });
        if (delete.ExitCode != 0)
            return CommandFailure("BI5520", operation.Id, delete, "delete IIS site");

        return new ResourceProviderResult { Message = "IIS site deleted through appcmd." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: IIS site verification would query appcmd." };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        return Detect(operation, context).Exists
            ? new ResourceProviderResult { Message = "IIS site exists." }
            : Error("BI5530", operation.Id, "IIS site verification failed because the site does not exist.");
    }

    private static string FirstBindingText(CompiledInstallOperation operation)
    {
        var first = Bindings(operation).FirstOrDefault();
        return first == null ? "http/*:80:" : BindingText(first);
    }

    private static IEnumerable<IisBindingInput> Bindings(CompiledInstallOperation operation)
    {
        var count = IntInput(operation, "bindingCount");
        for (var i = 0; i < count; i++)
        {
            yield return new IisBindingInput(
                i,
                FirstNonEmpty(Input(operation, $"binding.{i}.protocol"), "http"),
                FirstNonEmpty(Input(operation, $"binding.{i}.ipAddress"), "*"),
                IntInput(operation, $"binding.{i}.port", Input(operation, $"binding.{i}.protocol").Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80),
                Input(operation, $"binding.{i}.host"),
                Input(operation, $"binding.{i}.certificateThumbprint").Replace(" ", "", StringComparison.Ordinal),
                FirstNonEmpty(Input(operation, $"binding.{i}.certificateStoreName"), "My"),
                IntInput(operation, $"binding.{i}.sslFlags"));
        }
    }

    private static string BindingText(IisBindingInput binding)
        => $"{binding.Protocol}/{binding.IpAddress}:{binding.Port}:{binding.Host}";

    private static string BindingCollectionText(IisBindingInput binding)
    {
        var text = $"[protocol='{binding.Protocol}',bindingInformation='{binding.IpAddress}:{binding.Port}:{binding.Host}'";
        if (binding.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(binding.CertificateThumbprint))
        {
            text += $",certificateHash='{binding.CertificateThumbprint}',certificateStoreName='{binding.CertificateStoreName}',sslFlags='{binding.SslFlags}'";
        }

        return text + "]";
    }

    private static bool AlreadyExists(IisCommandResult result)
        => result.StandardOutput.Contains("already", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("already", StringComparison.OrdinalIgnoreCase)
           || result.StandardOutput.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static int IntInput(CompiledInstallOperation operation, string key, int defaultValue = 0)
        => operation.Inputs.TryGetValue(key, out var value)
           && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

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

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static bool IsNotFound(IisCommandResult result)
        => result.StandardOutput.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("Cannot find", StringComparison.OrdinalIgnoreCase);

    private ResourceProviderResult EnsureIisSupport(
        ResourceProviderContext context,
        IReadOnlyList<string> requiredFeatures,
        string errorCode,
        string path)
    {
        if (_runner.IsSupported)
            return new ResourceProviderResult();

        if (!CanEnableRequiredFeatures(context, requiredFeatures, out var policyError))
            return Error(errorCode, path, policyError);

        var enablement = _featureManager.EnableFeatures(requiredFeatures);
        if (!enablement.Success)
            return Error(errorCode, path, enablement.Message);

        return _runner.IsSupported
            ? new ResourceProviderResult
            {
                Evidence =
                {
                    ["iisFeatureEnablement"] = "enabled",
                    ["iisFeatures"] = string.Join(",", requiredFeatures)
                }
            }
            : Error(errorCode, path, "IIS feature enablement completed but appcmd.exe is still unavailable.");
    }

    private static bool CanEnableRequiredFeatures(
        ResourceProviderContext context,
        IReadOnlyList<string> requiredFeatures,
        out string error)
    {
        var policy = context.Policy;
        if (policy?.AllowIisFeatureEnablement != true)
        {
            error = "IIS site operations require Windows IIS appcmd support. Set policy AllowIisFeatureEnablement=true to let the installer enable required IIS Windows features.";
            return false;
        }

        if (policy.AllowedIisFeatures.Count > 0)
        {
            var denied = requiredFeatures
                .Where(feature => !policy.AllowedIisFeatures.Contains(feature, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (denied.Count > 0)
            {
                error = $"IIS feature enablement is blocked by policy. Add these required IIS features to AllowedIisFeatures: {string.Join(", ", denied)}.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool IsValidThumbprint(string value)
    {
        var normalized = value.Replace(" ", "", StringComparison.Ordinal);
        return normalized.Length == 40 && normalized.All(Uri.IsHexDigit);
    }

    private static bool IsValidCertificateStoreName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

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

    private static ResourceProviderResult CommandFailure(string code, string path, IisCommandResult result, string verb)
    {
        var message = $"Failed to {verb}. appcmd.exe exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";
        return Error(code, path, message);
    }

    private sealed record IisBindingInput(
        int Index,
        string Protocol,
        string IpAddress,
        int Port,
        string Host,
        string CertificateThumbprint,
        string CertificateStoreName,
        int SslFlags);
}

internal sealed class AppCmdIisCommandRunner : IIisCommandRunner
{
    private static readonly string AppCmdPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "inetsrv",
        "appcmd.exe");

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(AppCmdPath);

    public IisCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new IisCommandResult(-1, "", "IIS appcmd.exe is not available. Install IIS Management Scripts and Tools.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = AppCmdPath,
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

        return new IisCommandResult(process.ExitCode, standardOutput, standardError);
    }
}

internal sealed class DismIisFeatureManager : IIisFeatureManager
{
    public IisFeatureEnablementResult EnableFeatures(IReadOnlyList<string> featureNames)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new IisFeatureEnablementResult(false, "IIS feature enablement requires Windows.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dism.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.StartInfo.ArgumentList.Add("/Online");
        process.StartInfo.ArgumentList.Add("/Enable-Feature");
        process.StartInfo.ArgumentList.Add("/All");
        process.StartInfo.ArgumentList.Add("/NoRestart");
        foreach (var feature in featureNames.Distinct(StringComparer.OrdinalIgnoreCase))
            process.StartInfo.ArgumentList.Add($"/FeatureName:{feature}");

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode is 0 or 3010)
            return new IisFeatureEnablementResult(true, process.ExitCode == 3010
                ? "IIS Windows features were enabled; restart is required."
                : "IIS Windows features were enabled.");

        var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        return new IisFeatureEnablementResult(false, $"Failed to enable IIS Windows features. dism.exe exited with code {process.ExitCode}. {detail.Trim()}");
    }
}
