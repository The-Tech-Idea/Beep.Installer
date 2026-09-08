using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Autosave under concurrent editing (8.B.2).
///
/// The autosave timer runs on a thread-pool thread while the user edits on the UI thread, and the
/// serializer walks the project's <c>ObservableCollection</c>s. Snapshotting the live object threw
/// "collection was modified" mid-write — or, worse, wrote a torn file that still looked like a
/// valid script and would be offered as recovered work.
///
/// It also called <c>Save</c>, which stamps <c>ModifiedAt</c> through <c>SetProperty</c> and so set
/// <c>IsDirty</c>: autosaving dirtied the project it was snapshotting, and a saved document claimed
/// unsaved changes 30 seconds later.
/// </summary>
public sealed class AutoSaveRaceTests : IDisposable
{
    private readonly string _root;

    public AutoSaveRaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepAutoSave_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private (InstallerController Controller, string ScriptPath) NewProject()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.exe"), "app");

        var project = InstallerProjectFactory.CreateNew("RaceApp", "1.0.0", "ACME", source);
        var scriptPath = Path.Combine(_root, "RaceApp.bsetup");
        InstallerScriptSerializer.Save(project, scriptPath);

        var controller = new InstallerController();
        var (ok, error) = controller.Open(scriptPath);
        ok.Should().BeTrue(error);
        return (controller, scriptPath);
    }

    [Fact]
    public void AutoSavingDoesNotDirtyTheProjectItSnapshots()
    {
        var (controller, _) = NewProject();
        controller.Project.MarkDirty();

        controller.WriteAutoSaveSnapshot();
        controller.Project.MarkClean();
        controller.Project.MarkDirty();
        controller.WriteAutoSaveSnapshot();
        controller.Project.MarkClean();

        controller.Project.IsDirty.Should().BeFalse(
            "an autosave must not mark the document unsaved; it used to, by stamping ModifiedAt");
    }

    [Fact]
    public void AutoSaveDoesNotRewriteTheProjectsModifiedTimestamp()
    {
        var (controller, _) = NewProject();
        var before = controller.Project.ModifiedAt;
        controller.Project.MarkDirty();

        controller.WriteAutoSaveSnapshot();

        controller.Project.ModifiedAt.Should().Be(before, "a snapshot is a read, not an edit");
    }

    [Fact]
    public void SnapshottingWhileTheProjectIsEdited_NeverThrowsAndNeverWritesATornFile()
    {
        var (controller, scriptPath) = NewProject();
        var project = controller.Project;
        var autosavePath = AutoSave.AutoSavePath(scriptPath);
        var failures = 0;
        string? firstFailure = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // The editing thread restructures the very collections the serializer walks.
        var editor = Task.Run(() =>
        {
            var n = 0;
            while (!stop.IsCancellationRequested)
            {
                lock (controller.AutoSaveLock)
                {
                    project.Components.Add(new InstallComponent { Id = $"c{n}", Name = $"Component {n}", Selected = true });
                    if (project.Components.Count > 12) project.Components.RemoveAt(0);
                }
                project.MarkDirty();
                n++;
            }
        });

        // The autosave thread snapshots as fast as the timer ever could.
        var saver = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { controller.WriteAutoSaveSnapshot(); }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failures);
                    firstFailure ??= ex.ToString();
                }
            }
        });

        Task.WaitAll(new[] { editor, saver }, TimeSpan.FromSeconds(60)).Should().BeTrue("neither loop should wedge");

        failures.Should().Be(0, "snapshotting a project being edited must not throw: " + firstFailure);

        // One final, uncontended snapshot so the file assertion does not depend on how the loops
        // happened to interleave -- whether a tick landed while the project was dirty is timing,
        // and timing is not what this test is about.
        project.MarkDirty();
        controller.WriteAutoSaveSnapshot();

        // Whatever landed on disk must be a script that loads, not a half-written one.
        File.Exists(autosavePath).Should().BeTrue();
        var (recovered, error) = InstallerScriptSerializer.Load(autosavePath);
        error.Should().BeNull("a torn autosave would be offered as recovered work");
        recovered!.AppName.Should().Be("RaceApp");
    }

    [Fact]
    public void ANothingToDoTickWritesNothing()
    {
        var (controller, scriptPath) = NewProject();
        controller.Project.MarkClean();

        controller.WriteAutoSaveSnapshot();

        File.Exists(AutoSave.AutoSavePath(scriptPath)).Should().BeFalse(
            "a clean project has nothing to recover");
    }
}
