using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Steps;

internal static class ResourceProviderRuntimeSupport
{
    public static BuiltInResourceProviderRegistry ResolveRegistry(
        SetupContext context,
        BuiltInResourceProviderRegistry? overrideRegistry = null)
    {
        if (overrideRegistry != null)
            return overrideRegistry;

        if (context.Properties.TryGetValue(InstallContextKeys.ResourceProviderRegistry, out var value)
            && value is BuiltInResourceProviderRegistry registry)
            return registry;

        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project?.Resources.Count > 0)
        {
            var root = context.TryGetProperty<string>(InstallContextKeys.ExtensionBundleRoot)
                ?? EmbeddedInstallerResources.DirectoryPath
                ?? throw new InvalidOperationException("Embedded extension bundle is unavailable.");
            var loaded = InstallerExtensionBundle.Load(root, context.TryGetProperty<InstallerPolicy>(InstallContextKeys.ResourcePolicy));
            context.Properties[InstallContextKeys.ResourceProviderRegistry] = loaded;
            return loaded;
        }

        return CreateDefaultRegistry();
    }

    public static BuiltInResourceProviderRegistry CreateDefaultRegistry()
        => BuiltInInstallerResourceProviders.CreateDefaultRegistry();

    public static ResourceProviderContext BuildContext(
        SetupContext context,
        InstallProject project,
        string installPath,
        bool replayRollback = false,
        ResourceExecutionJournalMetadata? installedMetadata = null)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.Properties.TryGetValue(InstallContextKeys.RuntimeVariables, out var runtimeVariablesValue)
            && runtimeVariablesValue is IReadOnlyDictionary<string, string> runtimeVariables)
        {
            foreach (var variable in runtimeVariables)
            {
                if (!string.IsNullOrWhiteSpace(variable.Key))
                    variables[variable.Key] = variable.Value;
            }
        }

        variables["InstallPath"] = installPath;
        variables["ProductName"] = installedMetadata?.ProductName ?? project.AppName ?? "";
        variables["Version"] = project.AppVersion ?? "";

        var payloadRoot = context.TryGetProperty<string>(InstallContextKeys.PayloadRoot);
        if (!string.IsNullOrWhiteSpace(payloadRoot))
            variables["PayloadRoot"] = payloadRoot;

        return new ResourceProviderContext
        {
            InstallRoot = installPath,
            ProductName = variables["ProductName"],
            ProductVersion = project.AppVersion ?? "",
            PerUser = context.Properties.TryGetValue(InstallContextKeys.PerUser, out var perUserValue)
                      && perUserValue is bool perUser && perUser,
            DryRun = context.Options?.DryRun == true,
            ReplayRollback = replayRollback,
            AttemptId = context.TryGetProperty<string>(InstallContextKeys.ResourceExecutionAttemptId)
                        ?? Guid.NewGuid().ToString("N"),
            ExecutionMode = context.TryGetProperty<string>(InstallContextKeys.ResourceExecutionMode) ?? "install",
            SecretProvider = context.TryGetProperty<ISecretProvider>(InstallContextKeys.ResourceSecretProvider)
                             ?? new CompositeSecretProvider(),
            Policy = context.TryGetProperty<InstallerPolicy>(InstallContextKeys.ResourcePolicy),
            ConditionFacts = context.Properties.TryGetValue(InstallContextKeys.ResourceConditionFacts, out var factsValue)
                             && factsValue is IInstallerConditionFacts facts
                ? facts
                : null,
            Variables = variables
        };
    }

    public static string ResolveJournalPath(SetupContext context, string installPath, InstallProject project)
    {
        var configuredPath = context.TryGetProperty<string>(InstallContextKeys.ResourceExecutionJournalPath);
        return ResourceExecutionJournalStore.ResolvePath(installPath, project.AppId, configuredPath);
    }
}
