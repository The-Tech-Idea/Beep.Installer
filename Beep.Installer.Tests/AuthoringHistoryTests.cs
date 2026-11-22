using System.Collections;
using System.Reflection;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AuthoringHistoryTests
{
    private static InstallProject Project() => InstallerProjectFactory.CreateNew("History app", "1.0.0", "Publisher", "");

    [Fact]
    public void AddEditRemoveUndoRedoPreserveIdentityAndSerializerContracts()
    {
        var project = Project(); var appId = project.AppId; var history = new AuthoringHistory(); history.Reset(project);
        project.WindowsServices.Add(new() { Name = "Original", ExecutablePath = "{app}\\app.exe" }); history.Record(project, "Add service");
        project.WindowsServices[0].Name = "Edited"; history.Record(project, "Edit service");
        project.WindowsServices.Clear(); history.Record(project, "Remove service");
        project = history.Undo(project)!.Project; project.WindowsServices.Single().Name.Should().Be("Edited");
        project = history.Undo(project)!.Project; project.WindowsServices.Single().Name.Should().Be("Original");
        project = history.Undo(project)!.Project; project.WindowsServices.Should().BeEmpty(); history.CanUndo.Should().BeFalse();
        project = history.Redo(project)!.Project; project = history.Redo(project)!.Project;
        project.WindowsServices.Single().Name.Should().Be("Edited"); project.AppId.Should().Be(appId); project.IsDirty.Should().BeTrue();
        InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project)).project!.WindowsServices.Single().Name.Should().Be("Edited");
    }

    [Fact]
    public void HistoryNeverStoresPlaintextCredentialsInAnySnapshot()
    {
        var project = Project(); var password = "literal-credential-73925";
        project.CodeSignCertificatePassword = password;
        project.WindowsServices.Add(new() { Name = "Service", Password = password, Arguments = "--token " + password });
        project.ScheduledTasks.Add(new() { Name = "Task", Password = password }); project.Certificates.Add(new() { Password = password });
        project.IisAppPools.Add(new() { Name = "Pool", Password = password });
        project.EnvironmentVariables.Add(new() { Name = "API_KEY", Value = password });
        project.CustomPages.Add(new() { Id = "page", Fields = { new() { Id = "pw", Type = CustomFieldType.Password, DefaultValue = password } } });
        project.Resources.Add(new() { Id = "provider", Type = "provider", Inputs = { ["opaque"] = password }, SensitiveInputs = { "opaque" } });
        var history = new AuthoringHistory(); history.Reset(project); project.AppVersion = "2.0.0"; history.Record(project, "Edit version");
        foreach (var json in SnapshotJson(history)) json.Should().NotContain(password);
        var restored = history.Undo(project)!; restored.MissingCredentials.Should().Be(0);
        restored.Project.CodeSignCertificatePassword.Should().Be(password); restored.Project.Resources.Single().Inputs["opaque"].Should().Be(password);
    }

    [Fact]
    public void LatestCredentialSurvivesUndoOfAResourceRenameWithoutRetainingItsPreviousValue()
    {
        var project = Project(); project.WindowsServices.Add(new() { Name = "Original", Password = "old-raw-password" });
        var history = new AuthoringHistory(); history.Reset(project);
        project.WindowsServices[0].Name = "Renamed"; history.Record(project, "Rename");
        project.WindowsServices[0].Password = "latest-raw-password"; history.Record(project, "Credential").Should().BeFalse();
        var restored = history.Undo(project)!; restored.Project.WindowsServices.Single().Name.Should().Be("Original");
        restored.Project.WindowsServices.Single().Password.Should().Be("latest-raw-password"); restored.MissingCredentials.Should().Be(0);
        foreach (var json in SnapshotJson(history)) json.Should().NotContain("old-raw-password").And.NotContain("latest-raw-password");
    }

    [Fact]
    public void UndoRemovedCredentialRequiresReentryButSecretReferencesCanBeRestored()
    {
        var project = Project(); project.WindowsServices.Add(new() { Name = "Plaintext", Password = "raw-password" });
        project.WindowsServices.Add(new() { Name = "Referenced", Password = "env:SERVICE_PASSWORD" });
        var history = new AuthoringHistory(); history.Reset(project); project.WindowsServices.Clear(); history.Record(project, "Remove services");
        var restored = history.Undo(project)!; restored.MissingCredentials.Should().Be(1);
        restored.Project.WindowsServices[0].Password.Should().BeEmpty(); restored.Project.WindowsServices[1].Password.Should().Be("env:SERVICE_PASSWORD");
    }

    [Fact]
    public void NewEditsClearRedoAndCapacityEvictsOldestTransactions()
    {
        var project = Project(); var history = new AuthoringHistory(2); history.Reset(project);
        foreach (var version in new[] { "2.0.0", "3.0.0", "4.0.0" }) { project.AppVersion = version; history.Record(project, "Version"); }
        project = history.Undo(project)!.Project; project = history.Undo(project)!.Project;
        project.AppVersion.Should().Be("2.0.0"); history.CanUndo.Should().BeFalse(); history.CanRedo.Should().BeTrue();
        project.AppName = "Branch"; history.Record(project, "Name"); history.CanRedo.Should().BeFalse();
        history.Reset(Project()); history.CanUndo.Should().BeFalse(); history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void SaveTimestampAndSecretOnlyEditsDoNotCreateUndoEntries()
    {
        var project = Project(); var history = new AuthoringHistory(); history.Reset(project);
        project.ModifiedAt = DateTime.UtcNow.ToString("o"); project.CodeSignCertificatePassword = "new-password";
        history.Record(project, "Save metadata").Should().BeFalse(); history.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReclassifyingSensitiveFieldsScrubsEarlierSnapshotsAndRemainsProtectedAfterUndo()
    {
        var project = Project(); const string secret = "newly-classified-private-value";
        project.Resources.Add(new() { Id = "provider", Type = "provider", Inputs = { ["opaque"] = secret } });
        project.CustomPages.Add(new() { Id = "page", Fields = { new() { Id = "field", Type = CustomFieldType.Text, DefaultValue = secret } } });
        var history = new AuthoringHistory(); history.Reset(project);
        project.AppVersion = "2.0.0"; history.Record(project, "Version");
        project.Resources[0].SensitiveInputs.Add("opaque"); project.CustomPages[0].Fields[0].Type = CustomFieldType.Password;
        history.Record(project, "Mark sensitive");
        foreach (var json in SnapshotJson(history)) json.Should().NotContain(secret);
        project = history.Undo(project)!.Project;
        project.Resources[0].SensitiveInputs.Should().BeEmpty(); project.Resources[0].Inputs["opaque"].Should().Be(secret);
        project.CustomPages[0].Fields[0].Type.Should().Be(CustomFieldType.Text);
        project.AppName = "Another edit"; history.Record(project, "Name");
        foreach (var json in SnapshotJson(history)) json.Should().NotContain(secret);
    }

    [Fact]
    public void ChangedAccountAndCertificateNeverReuseACredentialForTheWrongIdentity()
    {
        var project = Project(); project.WindowsServices.Add(new() { Name = "Service", Account = WindowsServiceAccount.User, Username = "old-user", Password = "old-secret" });
        project.CodeSignCertificatePath = "old.pfx"; project.CodeSignCertificatePassword = "old-pfx-password";
        var history = new AuthoringHistory(); history.Reset(project);
        project.WindowsServices[0].Username = "new-user"; project.WindowsServices[0].Password = "new-secret";
        project.CodeSignCertificatePath = "new.pfx"; project.CodeSignCertificatePassword = "new-pfx-password";
        history.Record(project, "Change identities"); var restored = history.Undo(project)!;
        restored.Project.WindowsServices[0].Username.Should().Be("old-user"); restored.Project.WindowsServices[0].Password.Should().BeEmpty();
        restored.Project.CodeSignCertificatePath.Should().Be("old.pfx"); restored.Project.CodeSignCertificatePassword.Should().BeEmpty();
        restored.MissingCredentials.Should().Be(2);
    }

    [Fact]
    public void AWholeGraphReplacementPreservesLiveCredentialsWhenNamedItemsKeepTheirIdentity()
    {
        var project = Project(); project.WindowsServices.Add(new() { Name = "Service", Password = "initial-secret" });
        project.Resources.Add(new() { Id = "provider", Type = "provider", Inputs = { ["opaque"] = "initial-input" }, SensitiveInputs = { "opaque" } });
        var history = new AuthoringHistory(); history.Reset(project);
        var replacement = InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project)).project!;
        replacement.AppVersion = "2.0.0"; replacement.WindowsServices[0].Password = "latest-secret"; replacement.Resources[0].Inputs["opaque"] = "latest-input";
        history.AdoptReplacement(project, replacement); history.Record(replacement, "Replace graph");
        var restored = history.Undo(replacement)!;
        restored.Project.AppVersion.Should().Be("1.0.0"); restored.Project.WindowsServices[0].Password.Should().Be("latest-secret");
        restored.Project.Resources[0].Inputs["opaque"].Should().Be("latest-input"); restored.MissingCredentials.Should().Be(0);
        foreach (var json in SnapshotJson(history)) json.Should().NotContain("initial-secret").And.NotContain("latest-secret").And.NotContain("initial-input").And.NotContain("latest-input");
    }

    [Fact]
    public void UncommittedSensitivityDeclarationScrubsHistoryBeforeRestore()
    {
        var project = Project(); const string secret = "unclassified-opaque-value";
        project.Resources.Add(new() { Id = "provider", Type = "provider", Inputs = { ["opaque"] = secret } });
        var history = new AuthoringHistory(); history.Reset(project);
        project.AppVersion = "2.0.0"; history.Record(project, "Version");
        project.Resources[0].SensitiveInputs.Add("opaque");
        project = history.Undo(project)!.Project;
        project.AppVersion.Should().Be("1.0.0"); project.Resources[0].Inputs["opaque"].Should().Be(secret);
        foreach (var json in SnapshotJson(history)) json.Should().NotContain(secret);
    }

    private static IEnumerable<string> SnapshotJson(AuthoringHistory history)
    {
        foreach (var stack in new[] { "_undo", "_redo" })
        foreach (var change in (IEnumerable)typeof(AuthoringHistory).GetField(stack, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(history)!)
        foreach (var side in new[] { "Before", "After" })
        {
            var snapshot = change.GetType().GetProperty(side)!.GetValue(change)!;
            yield return (string)snapshot.GetType().GetProperty("Json")!.GetValue(snapshot)!;
        }
    }
}
