using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SourceScanTransactionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BeepScanTransaction_" + Guid.NewGuid().ToString("N"));
    private readonly string[] _names = { "app.exe", "debug.pdb", "app.runtimeconfig.json" };
    public SourceScanTransactionTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, _names[0]), "inert payload");
        File.WriteAllText(Path.Combine(_directory, _names[1]), "symbols");
        File.WriteAllText(Path.Combine(_directory, _names[2]), "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}]}}");
    }
    public void Dispose() { foreach (var name in _names) File.Delete(Path.Combine(_directory, name)); Directory.Delete(_directory); }
    private InstallerController CreateController()
    {
        var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Publisher", "");
        project.Components.Clear(); project.SourceIncludes.Clear(); project.SourceIncludes.Add("*"); project.SourceExcludes.Clear(); project.SourceExcludes.Add("*.pdb");
        var core = new InstallComponent { Id = "core", Name = "Authored core", Required = true, Selected = true };
        core.Files.Add(new() { SourcePath = "previous.exe", DestinationPath = "app.exe", Description = "Authored description", IsRequired = false, SharedCount = true });
        project.Components.Add(core); project.Components.Add(new() { Id = "tools", Name = "Unrelated tools" });
        project.Prerequisites.Add(new() { Id = "manual", Name = "Authored prerequisite" });
        var controller = new InstallerController(project); controller.StopAutoSave(); project.MarkClean(); return controller;
    }

    [Fact]
    public async Task DetachedScanAppliesOnceOnTheCallerAndPreservesObjectsWithOneUndoableTransaction()
    {
        var controller = CreateController(); var project = controller.Project; var core = project.Components[0];
        var file = core.Files[0]; var other = project.Components[1]; var prerequisite = project.Prerequisites[0];
        var before = InstallerScriptSerializer.Write(project); var notifications = new List<int>();
        controller.ProjectMutated += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
        var request = controller.CaptureSourceScan(_directory); controller.TryApplySourceScan(request).Should().BeFalse("unrun captures are not results");
        request.Candidate.Should().NotBeSameAs(project); request.Candidate.Components[0].Files[0].Should().NotBeSameAs(file);
        var result = await Task.Run(() => request.Run());
        InstallerScriptSerializer.Write(project).Should().Be(before); notifications.Should().BeEmpty(); project.IsDirty.Should().BeFalse();
        var applyingThread = Environment.CurrentManagedThreadId;
        controller.TryApplySourceScan(request).Should().BeTrue(); notifications.Should().Equal(applyingThread);
        controller.TryApplySourceScan(request).Should().BeFalse(); notifications.Should().HaveCount(1);
        controller.Project.Should().BeSameAs(project); project.Components[0].Should().BeSameAs(core); project.Components[1].Should().BeSameAs(other);
        core.Files.Single(item => item.DestinationPath == "app.exe").Should().BeSameAs(file);
        file.SourcePath.Should().Be(Path.Combine(_directory, "app.exe")); file.Description.Should().Be("Authored description"); file.IsRequired.Should().BeFalse(); file.SharedCount.Should().BeTrue();
        core.Files.Should().NotContain(item => item.DestinationPath == "debug.pdb"); core.SizeBytes.Should().Be(result.TotalSizeBytes);
        project.Prerequisites[0].Should().BeSameAs(prerequisite); project.Prerequisites.Should().HaveCount(2);
        project.MainExecutable.Should().Be("app.exe"); project.SourceDirectory.Should().Be(_directory); project.IsDirty.Should().BeTrue();
        var after = InstallerScriptSerializer.Write(project); controller.Undo().Should().NotBeNull();
        InstallerScriptSerializer.Write(controller.Project).Should().Be(before); controller.CanUndo.Should().BeFalse();
        controller.Redo().Should().NotBeNull(); InstallerScriptSerializer.Write(controller.Project).Should().Be(after);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("source")]
    [InlineData("include")]
    [InlineData("exclude")]
    [InlineData("nested_file")]
    [InlineData("input")]
    public async Task EditsIncludingUnnotifiedNestedChangesRejectTheCompletedCandidate(string edit)
    {
        var controller = CreateController(); controller.Project.Resources.Add(new() { Id = "extension", Type = "file.copy" }); controller.CommitAuthoringChanges();
        var request = controller.CaptureSourceScan(_directory);
        switch (edit)
        {
            case "name": controller.Project.AppName = "Changed name"; break;
            case "source": controller.Project.SourceDirectory = "changed source"; break;
            case "include": controller.Project.SourceIncludes.Add("*.json"); break;
            case "exclude": controller.Project.SourceExcludes.Add("*.exe"); break;
            case "nested_file": controller.Project.Components[0].Files[0].Description = "Later file edit"; break;
            case "input": controller.Project.Resources[0].Inputs["destination"] = "Later input edit"; break;
        }
        var before = System.Text.Json.JsonSerializer.Serialize(controller.Project, new System.Text.Json.JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        var mutations = 0; controller.ProjectMutated += (_, _) => mutations++;
        await Task.Run(() => request.Run()); controller.TryApplySourceScan(request).Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(controller.Project, new System.Text.Json.JsonSerializerOptions { IgnoreReadOnlyProperties = true }).Should().Be(before); mutations.Should().Be(0);
    }

    [Fact]
    public async Task ProjectReplacementWithIdenticalDataStillRejectsTheOldScan()
    {
        var controller = CreateController(); var original = controller.Project; var before = InstallerScriptSerializer.Write(original);
        var request = controller.CaptureSourceScan(_directory); var replacement = controller.CaptureBuildProject(); controller.ReplaceProject(replacement);
        await Task.Run(() => request.Run()); controller.TryApplySourceScan(request).Should().BeFalse();
        controller.Project.Should().BeSameAs(replacement); InstallerScriptSerializer.Write(replacement).Should().Be(before); InstallerScriptSerializer.Write(original).Should().Be(before);
    }

    [Fact]
    public void LatestCaptureSupersedesAnEarlierRequestEvenBeforeItIsApplied()
    {
        var controller = CreateController(); var first = controller.CaptureSourceScan(_directory); var latest = controller.CaptureSourceScan(_directory);
        first.Run(); controller.TryApplySourceScan(first).Should().BeFalse(); latest.Run(); controller.TryApplySourceScan(latest).Should().BeTrue();
        controller.Project.Prerequisites.Count(item => item.Id != "manual").Should().Be(1);
        var fresh = controller.CaptureSourceScan(_directory); fresh.Run(); controller.TryApplySourceScan(fresh).Should().BeTrue();
        controller.Project.Prerequisites.Count(item => item.Id != "manual").Should().Be(1);
    }

    [Fact]
    public void FailedWorkerCannotApplyPartialCandidateMutations()
    {
        var controller = CreateController(); var before = InstallerScriptSerializer.Write(controller.Project); var request = controller.CaptureSourceScan(_directory);
        Action run = () => request.Run((candidate, _) => { candidate.Components.Clear(); candidate.AppName = "Partial mutation"; throw new IOException("scan failure"); });
        run.Should().Throw<IOException>(); controller.TryApplySourceScan(request).Should().BeFalse();
        InstallerScriptSerializer.Write(controller.Project).Should().Be(before); controller.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ScanCreatesOnlyTheMissingCoreAndKeepsAnAuthoredExecutableAndUnrelatedComponent()
    {
        var controller = CreateController(); controller.Project.Components.RemoveAt(0); var other = controller.Project.Components[0];
        controller.Project.MainExecutable = "authored.exe";
        var request = controller.CaptureSourceScan(_directory); request.Run(); controller.TryApplySourceScan(request).Should().BeTrue();
        controller.Project.Components.Should().HaveCount(2); controller.Project.Components[0].Id.Should().Be("core");
        controller.Project.Components[1].Should().BeSameAs(other); controller.Project.MainExecutable.Should().Be("authored.exe");
    }

    [Fact]
    public void SynchronousScanUsesTheSameDetachedApplicationAndPreservesAuthoredObjects()
    {
        var controller = CreateController(); var project = controller.Project; var component = project.Components[0]; var file = component.Files[0];
        var result = controller.Scan(_directory); result.FileCount.Should().Be(2); controller.Project.Should().BeSameAs(project);
        project.Components[0].Should().BeSameAs(component); component.Files.Single(item => item.DestinationPath == "app.exe").Should().BeSameAs(file);
    }
}
