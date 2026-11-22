using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class RecoveryProjectOpeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RecoveryOpening_" + Guid.NewGuid().ToString("N"));
    private readonly InstallerController _controller = new();
    private readonly string _saved;
    public RecoveryProjectOpeningTests()
    {
        Directory.CreateDirectory(_root); _controller.StopAutoSave();
        _saved = Path.Combine(_root, "saved.bsetup"); _controller.SaveAs(_saved).ok.Should().BeTrue();
        _controller.Project.AppVersion = "9.0.0"; _controller.WriteAutoSaveSnapshot();
        File.SetLastWriteTimeUtc(_controller.SaveState.RecoveryPath, File.GetLastWriteTimeUtc(_saved).AddSeconds(2));
    }
    public void Dispose() { _controller.StopAutoSave(); Directory.Delete(_root, true); }

    [Theory]
    [InlineData(DialogResult.Yes, "9.0.0", true)]
    [InlineData(DialogResult.No, "1.0.0", false)]
    public void InteractiveChoiceOpensRequestedVersionAndRetainsOriginalIdentity(DialogResult choice, string version, bool recovered)
    {
        var result = RecoveryProjectOpening.Open(_controller, _saved, chooseRecovery: () => choice);
        result.ok.Should().BeTrue(); result.canceled.Should().BeFalse();
        _controller.Project.AppVersion.Should().Be(version);
        _controller.SaveState.IsRecovered.Should().Be(recovered); _controller.IsDirty.Should().Be(recovered);
        _controller.FilePath.Should().Be(_saved); File.Exists(AutoSave.AutoSavePath(_saved)).Should().BeTrue();
    }

    [Theory]
    [InlineData(DialogResult.Cancel)]
    [InlineData(DialogResult.None)]
    public void CancelLeavesProjectAndSaveStateUntouched(DialogResult choice)
    {
        var project = _controller.Project; var saveState = _controller.SaveState;
        var result = RecoveryProjectOpening.Open(_controller, _saved, chooseRecovery: () => choice);
        result.canceled.Should().BeTrue(); result.ok.Should().BeFalse(); result.error.Should().BeNull();
        _controller.Project.Should().BeSameAs(project); _controller.SaveState.Should().Be(saveState);
    }

    [Fact]
    public void NoNewerDraftDoesNotAskAndOpensSavedProject()
    {
        File.SetLastWriteTimeUtc(AutoSave.AutoSavePath(_saved), File.GetLastWriteTimeUtc(_saved).AddSeconds(-2));
        var asked = false;
        RecoveryProjectOpening.Open(_controller, _saved, chooseRecovery: () => { asked = true; return DialogResult.Yes; }).ok.Should().BeTrue();
        asked.Should().BeFalse(); _controller.IsDirty.Should().BeFalse(); _controller.Project.AppVersion.Should().Be("1.0.0");
    }
}
