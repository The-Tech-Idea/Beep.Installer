using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ProjectFieldEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bsetup-fields-" + Guid.NewGuid().ToString("N"));
    private readonly string _script;

    public ProjectFieldEditorTests()
    {
        Directory.CreateDirectory(_dir);
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        _script = Path.Combine(_dir, "MyApp.bsetup");
        File.Copy(sample, _script);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Set_ChangesVersion_KeepsBackup_AndReloads()
    {
        var result = ProjectFieldEditor.Set(_script, new Dictionary<string, string> { ["AppVersion"] = "9.8.7" });
        result.Ok.Should().BeTrue(result.Error);
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();
        ProjectFieldEditor.List(_script).Values["AppVersion"].Should().Be("9.8.7");
    }

    [Fact]
    public void Set_UnknownField_IsRefused_AndFileUntouched()
    {
        var before = File.ReadAllText(_script);
        var result = ProjectFieldEditor.Set(_script, new Dictionary<string, string> { ["NoSuchField"] = "x" });
        result.Ok.Should().BeFalse();
        File.ReadAllText(_script).Should().Be(before);
    }

    [Fact]
    public void Set_BadBoolean_IsRefused()
    {
        var boolField = ProjectFieldEditor.List(_script).Values.First(v => bool.TryParse(v.Value, out _)).Key;
        ProjectFieldEditor.Set(_script, new Dictionary<string, string> { [boolField] = "maybe" }).Ok.Should().BeFalse();
    }
}
