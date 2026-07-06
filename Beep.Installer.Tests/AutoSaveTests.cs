using Beep.Installer.Models;
using System.IO;
using System.Threading;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase cross-cutting (X4) — crash-recovery auto-save path/timestamp logic.</summary>
public class AutoSaveTests
{
    [Fact]
    public void AutoSavePath_Sits_Beside_The_Project()
    {
        Engine.AutoSave.AutoSavePath(@"C:\dir\MyApp.bsetup").Should().Be(@"C:\dir\MyApp.autosave.bsetup");
    }

    [Fact]
    public void Recovery_Detects_Newer_Autosave()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepas_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var project = Path.Combine(tmp, "App.bsetup");
            var autosave = Engine.AutoSave.AutoSavePath(project);

            File.WriteAllText(project, "v1");
            Engine.AutoSave.IsRecoveryAvailable(project).Should().BeFalse("no autosave yet");

            File.WriteAllText(autosave, "v1-autosave");
            // Same timestamp resolution on some FSes — bump the autosave clearly into the future.
            File.SetLastWriteTimeUtc(autosave, File.GetLastWriteTimeUtc(project).AddSeconds(5));

            Engine.AutoSave.IsRecoveryAvailable(project).Should().BeTrue();

            Engine.AutoSave.Clear(project);
            File.Exists(autosave).Should().BeFalse();
            Engine.AutoSave.IsRecoveryAvailable(project).Should().BeFalse();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Recovery_NotAvailable_When_Project_Newer_Than_Autosave()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepas_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var project = Path.Combine(tmp, "App.bsetup");
            var autosave = Engine.AutoSave.AutoSavePath(project);

            File.WriteAllText(autosave, "old-autosave");
            Thread.Sleep(20);
            File.WriteAllText(project, "newer-project"); // project written after autosave

            Engine.AutoSave.IsRecoveryAvailable(project).Should().BeFalse();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }
}
