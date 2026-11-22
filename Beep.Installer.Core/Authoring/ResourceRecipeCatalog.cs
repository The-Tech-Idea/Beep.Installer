using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public enum ResourceRecipeKind { WindowsService, IisSite, ScheduledTask, FirewallRule }

public sealed record ResourceRecipeRequest(ResourceRecipeKind Kind, string Name, string TargetPath,
    string PoolName = "", int Port = 8080, string Host = "", string RuntimeVersion = "v4.0",
    ScheduledTaskTrigger Trigger = ScheduledTaskTrigger.OnLogon, string StartTime = "09:00",
    WindowsServiceAccount ServiceAccount = WindowsServiceAccount.LocalService,
    FirewallRuleProtocol FirewallProtocol = FirewallRuleProtocol.Tcp, string FirewallProfile = "domain,private",
    bool UseMachineScope = false, bool UseExeOutput = false);

public sealed record ResourceRecipeChange(string SectionId, string Path, string Name);
public sealed record ResourceRecipePreview(ResourceRecipeRequest Request, IReadOnlyList<ResourceRecipeChange> Changes,
    IReadOnlyList<ProjectSchemaDiagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

/// <summary>Authoring recipes emit the existing typed resources and use the existing schema/package contracts.</summary>
public static class ResourceRecipeCatalog
{
    public static ResourceRecipePreview Preview(InstallProject project, ResourceRecipeRequest request)
        => Prepare(project, request).Preview;

    public static ResourceRecipePreview Apply(InstallProject project, ResourceRecipeRequest request)
    {
        // Re-evaluate against the current graph: a previously valid preview is not authorization to overwrite a new item.
        var prepared = Prepare(project, request);
        if (!prepared.Preview.CanApply) return prepared.Preview;
        var previousScope = project.DefaultScope;
        var previousPrivileges = project.PrivilegesRequired;
        var previousScopeSelection = project.AllowScopeSelection;
        var previousPrivilegeOverrides = project.PrivilegesRequiredOverridesAllowed;
        var previousFormat = project.OutputFormat;
        var previousDirty = project.IsDirty;
        var undo = new Stack<Action>();
        try
        {
            if (request.UseMachineScope)
            {
                project.DefaultScope = InstallationScope.Machine; project.PrivilegesRequired = PrivilegeLevel.Admin;
                project.AllowScopeSelection = false; project.PrivilegesRequiredOverridesAllowed = false;
            }
            if (request.UseExeOutput) project.OutputFormat = InstallerOutputFormat.Exe;
            foreach (var change in prepared.Preview.Changes)
            {
                switch (change.SectionId)
                {
                    case "windowsservices": Add(project.WindowsServices, prepared.Candidate.WindowsServices.Last()); break;
                    case "scheduledtasks": Add(project.ScheduledTasks, prepared.Candidate.ScheduledTasks.Last()); break;
                    case "firewallrules": Add(project.FirewallRules, prepared.Candidate.FirewallRules.Last()); break;
                    case "iisapppools": Add(project.IisAppPools, prepared.Candidate.IisAppPools.Last()); break;
                    case "iissites": Add(project.IisSites, prepared.Candidate.IisSites.Last()); break;
                }
            }
            project.MarkDirty();
            return prepared.Preview;
        }
        catch
        {
            while (undo.TryPop(out var revert)) revert();
            project.DefaultScope = previousScope;
            project.PrivilegesRequired = previousPrivileges;
            project.AllowScopeSelection = previousScopeSelection;
            project.PrivilegesRequiredOverridesAllowed = previousPrivilegeOverrides;
            project.OutputFormat = previousFormat;
            if (!previousDirty) project.MarkClean();
            throw;
        }
        void Add<T>(System.Collections.ObjectModel.ObservableCollection<T> collection, T item)
        {
            undo.Push(() => collection.Remove(item));
            collection.Add(item);
        }
    }

    private static (ResourceRecipePreview Preview, InstallProject Candidate) Prepare(InstallProject project, ResourceRecipeRequest request)
    {
        var issues = new List<ProjectSchemaDiagnostic>();
        void Error(string code, string path, string message) => issues.Add(new(ProjectSchemaDiagnosticSeverity.Error, code, path, message));
        var (candidate, readError) = InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project), project.ProjectName);
        if (candidate is null)
        {
            Error("BIUI010", "", readError ?? "The project could not be captured for the recipe.");
            return (new(request, Array.Empty<ResourceRecipeChange>(), issues), project);
        }
        var changes = new List<ResourceRecipeChange>();
        if (request.UseMachineScope && project.DefaultScope != InstallationScope.Machine)
            changes.Add(new("layout", "Setup.DefaultScope", "Machine"));
        if (request.UseMachineScope && project.PrivilegesRequired != PrivilegeLevel.Admin)
            changes.Add(new("layout", "Setup.PrivilegesRequired", "Admin"));
        if (request.UseMachineScope && project.AllowScopeSelection)
            changes.Add(new("layout", "Setup.AllowScopeSelection", "False"));
        if (request.UseMachineScope && project.PrivilegesRequiredOverridesAllowed)
            changes.Add(new("layout", "Setup.PrivilegesRequiredOverridesAllowed", "False"));
        if (request.UseExeOutput && project.OutputFormat != InstallerOutputFormat.Exe)
            changes.Add(new("package", "Setup.OutputFormat", "Exe"));
        var name = request.Name.Trim();
        var path = request.TargetPath.Trim();
        if (!Enum.IsDefined(request.Kind)) Error("BIUI010", "Recipe.Kind", "Choose a supported resource recipe.");
        if (name.Length == 0 || name.Any(char.IsControl)) Error("BIUI010", "Recipe.Name", "A resource name without control characters is required.");
        if (request.UseMachineScope)
        {
            candidate.DefaultScope = InstallationScope.Machine; candidate.PrivilegesRequired = PrivilegeLevel.Admin;
            candidate.AllowScopeSelection = false; candidate.PrivilegesRequiredOverridesAllowed = false;
        }
        if (candidate.DefaultScope != InstallationScope.Machine || candidate.PrivilegesRequired != PrivilegeLevel.Admin ||
            candidate.AllowScopeSelection || candidate.PrivilegesRequiredOverridesAllowed)
            Error("BIUI011", "Setup.DefaultScope", "These recipes configure machine resources. Choose a machine installation with administrator privileges and disable scope/privilege overrides.");
        if (request.UseExeOutput) candidate.OutputFormat = InstallerOutputFormat.Exe;
        if (candidate.OutputFormat is not (InstallerOutputFormat.Exe or InstallerOutputFormat.Msi))
            Error("BIUI012", "Setup.OutputFormat", "These Windows resource recipes require EXE or MSI output. Choose EXE output or change the package selection.");

        switch (request.Kind)
        {
            case ResourceRecipeKind.WindowsService:
                if (!Enum.IsDefined(request.ServiceAccount) || request.ServiceAccount == WindowsServiceAccount.User)
                    Error("BIUI013", "Recipe.ServiceAccount", "Choose a built-in service account. Configure user accounts and secret references in the full resource editor.");
                candidate.WindowsServices.Add(new WindowsServiceDefinition { Name = name, DisplayName = name,
                    ExecutablePath = path, Account = request.ServiceAccount, StartMode = WindowsServiceStartMode.Auto,
                    StartAfterInstall = true, StopOnUninstall = true });
                changes.Add(new("windowsservices", $"WindowsServices[{project.WindowsServices.Count}]", name));
                break;
            case ResourceRecipeKind.ScheduledTask:
                if (!Enum.IsDefined(request.Trigger)) Error("BIUI013", "Recipe.Trigger", "Choose a supported task trigger.");
                candidate.ScheduledTasks.Add(new ScheduledTaskDefinition { Name = name, ExecutablePath = path,
                    WorkingDirectory = "{app}", Trigger = request.Trigger, StartTime = request.StartTime.Trim(),
                    RunElevated = false, Enabled = true, StopOnUninstall = true });
                changes.Add(new("scheduledtasks", $"ScheduledTasks[{project.ScheduledTasks.Count}]", name));
                break;
            case ResourceRecipeKind.FirewallRule:
                if (request.Port is < 1 or > 65535) Error("BIUI014", "Recipe.Port", "The port must be between 1 and 65535.");
                if (request.FirewallProtocol is not (FirewallRuleProtocol.Tcp or FirewallRuleProtocol.Udp))
                    Error("BIUI013", "Recipe.FirewallProtocol", "A port rule requires TCP or UDP.");
                candidate.FirewallRules.Add(new FirewallRuleDefinition { Name = name, Program = path,
                    Protocol = request.FirewallProtocol, LocalPort = request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Direction = FirewallRuleDirection.In, Action = FirewallRuleAction.Allow, Profile = request.FirewallProfile.Trim(),
                    Enabled = true, RemoveOnUninstall = true });
                changes.Add(new("firewallrules", $"FirewallRules[{project.FirewallRules.Count}]", name));
                break;
            case ResourceRecipeKind.IisSite:
                var pool = request.PoolName.Trim();
                if (pool.Length == 0) Error("BIUI010", "Recipe.PoolName", "A new application pool name is required.");
                if (request.Port is < 1 or > 65535) Error("BIUI014", "Recipe.Port", "The port must be between 1 and 65535.");
                if (project.IisSites.SelectMany(site => site.Bindings).Any(binding => binding.Protocol == IisBindingProtocol.Http &&
                    binding.Port == request.Port && binding.IpAddress == "*" && binding.Host.Equals(request.Host.Trim(), StringComparison.OrdinalIgnoreCase)))
                    Error("BIUI015", "Recipe.Port", "An authored IIS site already uses this HTTP binding. Choose a different port or host.");
                candidate.IisAppPools.Add(new IisAppPoolDefinition { Name = pool, RuntimeVersion = request.RuntimeVersion.Trim(),
                    Identity = "ApplicationPoolIdentity", PipelineMode = IisManagedPipelineMode.Integrated,
                    AutoStart = true, StartAfterInstall = true, RemoveOnUninstall = true });
                candidate.IisSites.Add(new IisSiteDefinition { Name = name, PhysicalPath = path, ApplicationPool = pool,
                    StartAfterInstall = true, RemoveOnUninstall = true,
                    Bindings = { new IisBindingDefinition { Protocol = IisBindingProtocol.Http, IpAddress = "*", Port = request.Port, Host = request.Host.Trim() } } });
                changes.Add(new("iisapppools", $"IisAppPools[{project.IisAppPools.Count}]", pool));
                changes.Add(new("iissites", $"IisSites[{project.IisSites.Count}]", name));
                break;
        }
        var baseline = ProjectSchemaService.Validate(project).Diagnostics.Select(Key).ToHashSet(StringComparer.Ordinal);
        issues.AddRange(ProjectSchemaService.Validate(candidate).Diagnostics.Where(d => !baseline.Contains(Key(d))));
        if (candidate.OutputFormat == InstallerOutputFormat.Msi) issues.AddRange(PackageTargetDiagnostics.Validate(candidate));
        return (new(request, changes, issues), candidate);
    }

    private static string Key(ProjectSchemaDiagnostic diagnostic) => $"{diagnostic.Code}|{diagnostic.Path}|{diagnostic.Message}";
}
