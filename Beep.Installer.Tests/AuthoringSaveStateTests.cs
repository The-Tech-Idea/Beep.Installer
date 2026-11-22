using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AuthoringSaveStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepSaveState_" + Guid.NewGuid().ToString("N"));
    private readonly InstallerController _controller = new();
    public AuthoringSaveStateTests() { Directory.CreateDirectory(_root); _controller.StopAutoSave(); }
    public void Dispose() { _controller.StopAutoSave(); Directory.Delete(_root, true); }

    [Fact]
    public void OpeningRelativePathsDoesNotCreateUnsavedChanges()
    {
        var original = Path.Combine(_root, "project.bsetup");
        _controller.Project.SourceDirectory = "relative-source";
        _controller.SaveAs(original).ok.Should().BeTrue();
        _controller.Open(original).ok.Should().BeTrue();
        _controller.Project.SourceDirectory.Should().Be(Path.Combine(_root, "relative-source"));
        _controller.IsDirty.Should().BeFalse();
        _controller.SaveState.LastSavedAt.Should().NotBeNull();
    }

    [Fact]
    public void FailedSaveAsRetainsOriginalIdentityAndRecoveryDraft()
    {
        var original = Path.Combine(_root, "original.bsetup");
        _controller.SaveAs(original).ok.Should().BeTrue();
        _controller.Project.AppName = "Edited";
        _controller.WriteAutoSaveSnapshot();
        var occupied = Path.Combine(_root, "occupied.bsetup");
        Directory.CreateDirectory(occupied);
        _controller.SaveAs(occupied).ok.Should().BeFalse();
        _controller.FilePath.Should().Be(original);
        _controller.IsDirty.Should().BeTrue();
        _controller.SaveState.SaveError.Should().NotBeNullOrEmpty();
        File.Exists(_controller.SaveState.RecoveryPath).Should().BeTrue();
        InstallerScriptSerializer.Load(original).project!.AppName.Should().NotBe("Edited");
    }

    [Fact]
    public void RecoveryLoadsDraftAsUnsavedAndKeepsOriginalIdentity()
    {
        var original = Path.Combine(_root, "project.bsetup");
        _controller.SaveAs(original).ok.Should().BeTrue();
        _controller.Project.AppName = "Recovered app";
        _controller.Project.SourceDirectory = "relative-source";
        _controller.WriteAutoSaveSnapshot();
        var recovery = _controller.SaveState.RecoveryPath;
        File.SetLastWriteTimeUtc(recovery, DateTime.UtcNow.AddMinutes(1));
        _controller.Open(original).ok.Should().BeTrue();
        _controller.Project.AppName.Should().Be("Recovered app");
        _controller.Project.SourceDirectory.Should().Be(Path.Combine(_root, "relative-source"));
        _controller.IsDirty.Should().BeTrue();
        _controller.SaveState.IsRecovered.Should().BeTrue();
        _controller.FilePath.Should().Be(original);
        File.Exists(recovery).Should().BeTrue();
        _controller.Save().ok.Should().BeTrue();
        _controller.SaveState.IsRecovered.Should().BeFalse();
        _controller.SaveState.LastSavedAt.Should().NotBeNull();
        _controller.IsDirty.Should().BeFalse();
        File.Exists(recovery).Should().BeFalse();
    }

    [Fact]
    public void InvalidRecoveryIsDisclosedAndPreserved()
    {
        var original = Path.Combine(_root, "project.bsetup");
        _controller.SaveAs(original).ok.Should().BeTrue();
        var recovery = _controller.SaveState.RecoveryPath;
        File.WriteAllText(recovery, "not a valid installer script");
        File.SetLastWriteTimeUtc(recovery, DateTime.UtcNow.AddMinutes(1));
        _controller.Open(original).ok.Should().BeTrue();
        _controller.SaveState.RecoveryError.Should().NotBeNullOrEmpty();
        _controller.SaveState.IsRecovered.Should().BeFalse();
        File.Exists(recovery).Should().BeTrue();
    }

    [Fact]
    public void AutosaveFailureIsVisibleAndRetryClearsFailure()
    {
        var original = Path.Combine(_root, "project.bsetup");
        _controller.SaveAs(original).ok.Should().BeTrue();
        _controller.Project.AppName = "Edited";
        var recovery = _controller.SaveState.RecoveryPath;
        Directory.CreateDirectory(recovery);
        _controller.WriteAutoSaveSnapshot();
        _controller.SaveState.AutoSaveError.Should().NotBeNullOrEmpty();
        _controller.IsDirty.Should().BeTrue();
        Directory.Delete(recovery);
        _controller.WriteAutoSaveSnapshot();
        _controller.SaveState.AutoSaveError.Should().BeNull();
        _controller.SaveState.LastAutoSavedAt.Should().NotBeNull();
        _controller.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task SavingDuringAutosaveLeavesNoStaleRecoveryAfterSave()
    {
        var original = Path.Combine(_root, "project.bsetup");
        _controller.SaveAs(original).ok.Should().BeTrue();
        _controller.Project.AppName = "Edited";
        await Task.WhenAll(Task.Run(() => { for (var i = 0; i < 100; i++) _controller.WriteAutoSaveSnapshot(); }),
            Task.Run(() => _controller.Save().ok.Should().BeTrue()));
        _controller.IsDirty.Should().BeFalse();
        File.Exists(_controller.SaveState.RecoveryPath).Should().BeFalse();
        InstallerScriptSerializer.Load(original).project!.AppName.Should().Be("Edited");
    }
}
