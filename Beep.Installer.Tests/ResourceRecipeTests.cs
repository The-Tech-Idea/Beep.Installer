using System.Collections.Specialized;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ResourceRecipeTests
{
    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Publisher", Environment.CurrentDirectory);
        project.AllowScopeSelection = false;
        return project;
    }
    private static ResourceRecipeRequest Request(ResourceRecipeKind kind) => new(kind, "ContosoResource", "{app}\\app.exe",
        PoolName: "ContosoPool", Port: 8080, Host: "app.example.test");

    [Theory]
    [InlineData(ResourceRecipeKind.WindowsService, "service.install")]
    [InlineData(ResourceRecipeKind.ScheduledTask, "scheduled-task.create")]
    [InlineData(ResourceRecipeKind.FirewallRule, "firewall.rule")]
    [InlineData(ResourceRecipeKind.IisSite, "iis.site")]
    public void RecipesUseExistingTypedDefinitionsAndCompileThroughExistingOperations(ResourceRecipeKind kind, string operationType)
    {
        var project = Project();
        var id = project.AppId;
        var preview = ResourceRecipeCatalog.Apply(project, Request(kind));
        preview.CanApply.Should().BeTrue(string.Join("; ", preview.Diagnostics.Select(d => d.Message)));
        project.AppId.Should().Be(id);
        project.IsDirty.Should().BeTrue();
        var (loaded, error) = InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project));
        error.Should().BeNull();
        loaded.Should().NotBeNull();
        var compiled = new InstallPlanCompiler().Compile(loaded!);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        compiled.Plan!.Operations.Should().Contain(operation => operation.Type == operationType);
        if (kind == ResourceRecipeKind.IisSite)
        {
            loaded!.IisAppPools.Should().ContainSingle(pool => pool.Name == "ContosoPool");
            loaded.IisSites.Should().ContainSingle(site => site.ApplicationPool == "ContosoPool" && site.Bindings.Single().Port == 8080);
            compiled.Plan.Operations.Single(operation => operation.Type == "iis.site").DependsOn.Should().NotBeEmpty();
        }
        if (kind == ResourceRecipeKind.WindowsService)
            loaded!.WindowsServices.Single().Account.Should().Be(WindowsServiceAccount.LocalService);
    }

    [Fact]
    public void PreviewDoesNotMutateTheOriginalProjectOrDirtyState()
    {
        var project = Project();
        project.DefaultScope = InstallationScope.User;
        project.PrivilegesRequired = PrivilegeLevel.Lowest;
        project.OutputFormat = InstallerOutputFormat.ClickOnce;
        project.MarkClean();
        var before = InstallerScriptSerializer.Write(project);
        var preview = ResourceRecipeCatalog.Preview(project, Request(ResourceRecipeKind.IisSite) with { UseMachineScope = true, UseExeOutput = true });
        preview.CanApply.Should().BeTrue();
        preview.Changes.Should().Contain(change => change.Path == "Setup.DefaultScope");
        preview.Changes.Should().Contain(change => change.Path == "Setup.PrivilegesRequired");
        preview.Changes.Should().Contain(change => change.Path == "Setup.OutputFormat");
        preview.Changes.Should().Contain(change => change.SectionId == "iisapppools");
        preview.Changes.Should().Contain(change => change.SectionId == "iissites");
        InstallerScriptSerializer.Write(project).Should().Be(before);
        project.IsDirty.Should().BeFalse();
    }

    [Theory]
    [InlineData(ResourceRecipeKind.WindowsService)]
    [InlineData(ResourceRecipeKind.ScheduledTask)]
    [InlineData(ResourceRecipeKind.FirewallRule)]
    [InlineData(ResourceRecipeKind.IisSite)]
    public void DuplicateNamesCannotOverwriteAuthoredResources(ResourceRecipeKind kind)
    {
        var project = Project();
        ResourceRecipeCatalog.Apply(project, Request(kind)).CanApply.Should().BeTrue();
        var before = InstallerScriptSerializer.Write(project);
        var repeated = ResourceRecipeCatalog.Apply(project, Request(kind) with { Name = "contosoresource" });
        repeated.CanApply.Should().BeFalse();
        InstallerScriptSerializer.Write(project).Should().Be(before);
    }

    [Fact]
    public void StalePreviewIsRevalidatedBeforeApplyingIisPair()
    {
        var project = Project();
        var request = Request(ResourceRecipeKind.IisSite);
        ResourceRecipeCatalog.Preview(project, request).CanApply.Should().BeTrue();
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = request.PoolName });
        var before = InstallerScriptSerializer.Write(project);
        ResourceRecipeCatalog.Apply(project, request).CanApply.Should().BeFalse();
        project.IisSites.Should().BeEmpty();
        project.IisAppPools.Should().ContainSingle();
        InstallerScriptSerializer.Write(project).Should().Be(before);
    }

    [Fact]
    public void ScopeAndOutputChangesRequireExplicitOptions()
    {
        var project = Project();
        project.DefaultScope = InstallationScope.User;
        project.PrivilegesRequired = PrivilegeLevel.Lowest;
        project.OutputFormat = InstallerOutputFormat.ClickOnce;
        var request = Request(ResourceRecipeKind.WindowsService);
        ResourceRecipeCatalog.Apply(project, request).CanApply.Should().BeFalse();
        project.WindowsServices.Should().BeEmpty();
        project.DefaultScope.Should().Be(InstallationScope.User);
        project.OutputFormat.Should().Be(InstallerOutputFormat.ClickOnce);
        ResourceRecipeCatalog.Apply(project, request with { UseMachineScope = true, UseExeOutput = true }).CanApply.Should().BeTrue();
        project.DefaultScope.Should().Be(InstallationScope.Machine);
        project.PrivilegesRequired.Should().Be(PrivilegeLevel.Admin);
        project.OutputFormat.Should().Be(InstallerOutputFormat.Exe);
        project.AllowScopeSelection.Should().BeFalse();
        project.PrivilegesRequiredOverridesAllowed.Should().BeFalse();
    }

    [Fact]
    public void RequiredFieldsAndInvalidPortOrTimeBlockApplyWithoutPartialResources()
    {
        var project = Project();
        foreach (var request in new[]
        {
            Request(ResourceRecipeKind.WindowsService) with { Name = "" },
            Request(ResourceRecipeKind.WindowsService) with { TargetPath = "" },
            Request(ResourceRecipeKind.WindowsService) with { ServiceAccount = WindowsServiceAccount.User },
            Request(ResourceRecipeKind.IisSite) with { PoolName = "" },
            Request(ResourceRecipeKind.IisSite) with { TargetPath = "" },
            Request(ResourceRecipeKind.FirewallRule) with { Port = 0 },
            Request(ResourceRecipeKind.FirewallRule) with { FirewallProtocol = FirewallRuleProtocol.Any },
            Request(ResourceRecipeKind.FirewallRule) with { FirewallProfile = "wrong-profile" },
            Request(ResourceRecipeKind.ScheduledTask) with { Trigger = ScheduledTaskTrigger.Daily, StartTime = "25:90" }
        })
        {
            var before = InstallerScriptSerializer.Write(project);
            ResourceRecipeCatalog.Apply(project, request).CanApply.Should().BeFalse();
            InstallerScriptSerializer.Write(project).Should().Be(before);
        }
    }

    [Fact]
    public void IisBindingConflictIsReportedBeforeAddingPoolOrSite()
    {
        var project = Project();
        project.IisSites.Add(new IisSiteDefinition { Name = "Existing", PhysicalPath = "{app}",
            Bindings = { new IisBindingDefinition { IpAddress = "*", Port = 8080, Host = "app.example.test" } } });
        var result = ResourceRecipeCatalog.Apply(project, Request(ResourceRecipeKind.IisSite));
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "BIUI015");
        project.IisAppPools.Should().BeEmpty();
        project.IisSites.Should().ContainSingle();
    }

    [Fact]
    public void UnrelatedExistingErrorsDoNotPreventAddingValidRecipe()
    {
        var project = Project();
        project.WindowsServices.Add(new WindowsServiceDefinition { Name = "Unrelated existing service", ExecutablePath = "" });
        ResourceRecipeCatalog.Apply(project, Request(ResourceRecipeKind.ScheduledTask)).CanApply.Should().BeTrue();
        project.ScheduledTasks.Should().ContainSingle();
        ProjectSchemaService.Validate(project).Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error);
    }

    [Fact]
    public void FailedIisCommitRestoresCollectionsSettingsAndCleanState()
    {
        var project = Project();
        project.DefaultScope = InstallationScope.User;
        project.PrivilegesRequired = PrivilegeLevel.Lowest;
        project.OutputFormat = InstallerOutputFormat.ClickOnce;
        project.MarkClean();
        var before = InstallerScriptSerializer.Write(project);
        project.IisSites.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Add) throw new InvalidOperationException("Observer failed"); };
        Action apply = () => ResourceRecipeCatalog.Apply(project, Request(ResourceRecipeKind.IisSite) with { UseMachineScope = true, UseExeOutput = true });
        apply.Should().Throw<InvalidOperationException>();
        project.IisAppPools.Should().BeEmpty();
        project.IisSites.Should().BeEmpty();
        project.IsDirty.Should().BeFalse();
        InstallerScriptSerializer.Write(project).Should().Be(before);
    }
}
