using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ComponentFileScanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OkWaitsForCurrentSizesAndClosingWhileWaitingPreventsApply(bool cancel)
    {
        Sta(() =>
        {
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var component = new InstallComponent(); component.Files.Add(new FileCopyOperation { SourcePath = "owned-inert.txt" });
            using var dialog = new ComponentFilesDialog(component, null);
            dialog.ReadSizes = (_, _, _) => { started.Set(); release.Wait(); return new(123, 0); };
            var applied = 0; dialog.ApplyDraft = () => { applied++; dialog.ReadySizeBytes.Should().Be(123); return true; };
            dialog.Show(); var size = dialog.PendingSize;
            try
            {
                UiStaTest.Pump(Task.Run(() => started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue()));
                dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().PerformClick();
                dialog.PendingApply.IsCompleted.Should().BeFalse(); applied.Should().Be(0);
                dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().Enabled.Should().BeFalse();
                if (cancel) dialog.Controls.Find("ComponentFilesCancel", true).OfType<Button>().Single().PerformClick();
                release.Set(); UiStaTest.Pump(size); UiStaTest.Pump(dialog.PendingApply);
                applied.Should().Be(cancel ? 0 : 1);
                dialog.DialogResult.Should().Be(cancel ? DialogResult.Cancel : DialogResult.OK);
            }
            finally { release.Set(); UiStaTest.Pump(size); UiStaTest.Pump(dialog.PendingApply); }
        });
    }

    [Fact]
    public void ApplyFailureKeepsTheDraftOpenWithLocalizedAccessibleFeedback()
    {
        Sta(() =>
        {
            using var dialog = new ComponentFilesDialog(new InstallComponent(), null); dialog.Show(); UiStaTest.Pump(dialog.PendingSize);
            dialog.ApplyDraft = () => throw new InvalidOperationException("Owned apply failure");
            dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().PerformClick(); UiStaTest.Pump(dialog.PendingApply);
            dialog.Visible.Should().BeTrue(); dialog.DialogResult.Should().Be(DialogResult.None);
            var status = dialog.Controls.Find("ComponentFileDropStatus", true).OfType<Label>().Single();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                status.Text.Should().Contain(LanguageManager.GetString("Files_ApplyFailed")).And.Contain("Owned apply failure");
                status.AccessibleName.Should().Be(status.Text);
            }
            dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().Enabled.Should().BeTrue();
        });
    }

    [Fact]
    public void LongScanDetailsScrollWithoutPushingActionsOutsideTheMinimumWindow()
    {
        Sta(() =>
        {
            using var font = new System.Drawing.Font("Segoe UI", 15);
            using var dialog = new ComponentFilesDialog(new InstallComponent(), null);
            dialog.Font = font; dialog.Size = dialog.MinimumSize; dialog.Show(); UiStaTest.Pump(dialog.PendingSize);
            var issues = Enumerable.Range(0, 1000).Select(i => "Owned detail " + i + " — " + new string('x', 120) + " & literal").ToArray();
            dialog.ReadDrop = (_, _, _, _) => new(Array.Empty<FileCopyOperation>(), 0, 1000, 0, issues);
            dialog.BeginDrop(new[] { "inert" }); UiStaTest.Pump(dialog.PendingDrop);
            var details = dialog.Controls.Find("ComponentFileDropDetails", true).OfType<TextBox>().Single();
            details.ReadOnly.Should().BeTrue(); details.ScrollBars.Should().Be(ScrollBars.Vertical);
            details.Text.Should().Contain(issues[0]).And.Contain(issues[^1]);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents(); dialog.PerformLayout();
                details.AccessibleName.Should().Be(LanguageManager.GetString("Files_DropIssues"));
                foreach (var name in new[] { "ComponentFilesOk", "ComponentFilesCancel" })
                {
                    var button = dialog.Controls.Find(name, true).Single();
                    var bounds = new System.Drawing.Rectangle(dialog.PointToClient(button.PointToScreen(System.Drawing.Point.Empty)), button.Size);
                    dialog.ClientRectangle.Contains(bounds).Should().BeTrue();
                }
            }
        });
    }

    [Fact]
    public void ActualBuilderModalDropAppliesOnceWithSourceRelativeSizesAndUndoRedo()
    {
        Sta(() => WithFixture((root, folder, first, nested, conflict) =>
        {
            var project = InstallerProjectFactory.CreateNew("Drop test", "1.0.0", "Publisher", "");
            project.SourceDirectory = root; project.Components.Clear();
            var component = new InstallComponent { Id = "core", Name = "Core" };
            component.Files.Add(new FileCopyOperation { SourcePath = Path.GetRelativePath(root, first), DestinationPath = Path.GetRelativePath(root, first), SharedCount = true });
            project.Components.Add(component);
            var controller = new InstallerController(project);
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("components");
            var before = InstallerScriptSerializer.Write(controller.Project);
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.OfType<ComponentFilesDialog>().FirstOrDefault();
                if (dialog is null) return; timer.Stop();
                try
                {
                    var grid = dialog.Controls.Find("ComponentFileGrid", true).OfType<DataGridView>().Single();
                    Raise(grid, "OnDragDrop", new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { folder }), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy));
                    UiStaTest.Pump(dialog.PendingDrop); UiStaTest.Pump(dialog.PendingSize);
                    InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                    dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().PerformClick();
                    UiStaTest.Pump(dialog.PendingApply);
                }
                catch (Exception ex) { failure = ex; }
                finally { if (dialog.DialogResult != DialogResult.OK) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); } }
            };
            timer.Start();
            Descendants(editor.ActiveSection!).OfType<Button>().Single(button => button.Text == LanguageManager.GetString("Builder_EditFiles")).PerformClick();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            controller.Project.Components[0].Files.Should().HaveCount(2);
            controller.Project.Components[0].Files[0].SharedCount.Should().BeTrue();
            controller.Project.Components[0].SizeBytes.Should().Be(new FileInfo(first).Length + new FileInfo(nested).Length);
            var after = InstallerScriptSerializer.Write(controller.Project);
            controller.Undo().Should().NotBeNull(); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            controller.Redo().Should().NotBeNull(); InstallerScriptSerializer.Write(controller.Project).Should().Be(after);
        }));
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
    [Fact]
    public void GridDropStagesFoldersAndReportsDuplicatesConflictsAndMissingPathsWithoutWritingTheComponent()
    {
        Sta(() => WithFixture((root, folder, first, nested, conflict) =>
        {
            var component = new InstallComponent { Id = "core" };
            component.Files.Add(new FileCopyOperation { SourcePath = Path.GetRelativePath(root, first),
                DestinationPath = "./assets/main.txt", Description = "Keep", SharedCount = true, Overwrite = false });
            component.Files.Add(new FileCopyOperation { SourcePath = "different.txt", DestinationPath = "conflict.txt" });
            var before = JsonSerializer.Serialize(component);
            using var dialog = new ComponentFilesDialog(component, root); dialog.Show(); UiStaTest.Pump(dialog.PendingSize);
            var original = JsonSerializer.Serialize(dialog.ResultFiles[0]);
            var grid = dialog.Controls.Find("ComponentFileGrid", true).OfType<DataGridView>().Single();
            var text = new DragEventArgs(new DataObject(DataFormats.Text, "inert text"), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            Raise(grid, "OnDragEnter", text); text.Effect.Should().Be(DragDropEffects.None);
            var drop = new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { folder, first, conflict, Path.Combine(root, "missing.txt") }),
                0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            Raise(grid, "OnDragEnter", drop); drop.Effect.Should().Be(DragDropEffects.Copy);
            Raise(grid, "OnDragDrop", drop); UiStaTest.Pump(dialog.PendingDrop); UiStaTest.Pump(dialog.PendingSize);
            dialog.ResultFiles.Should().HaveCount(3);
            dialog.ResultFiles.Should().Contain(file => file.SourcePath == nested && file.DestinationPath == Path.GetRelativePath(root, nested));
            JsonSerializer.Serialize(dialog.ResultFiles[0]).Should().Be(original);
            JsonSerializer.Serialize(component).Should().Be(before);
            var status = dialog.Controls.Find("ComponentFileDropStatus", true).OfType<Label>().Single();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                status.Text.Should().Contain(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Files_DropSummary"), 1, 2, 1, 1));
                status.AccessibleName.Should().Be(status.Text);
            }
            dialog.Controls.Find("ComponentFilesCancel", true).OfType<Button>().Single().PerformClick();
            JsonSerializer.Serialize(component).Should().Be(before);
        }));
    }

    [Fact]
    public void SlowSizeReaderRunsOffTheUiThreadAndOldResultsCannotReplaceTheEditedPathSize()
    {
        Sta(() =>
        {
            var thread = Environment.CurrentManagedThreadId;
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var component = new InstallComponent(); component.Files.Add(new FileCopyOperation { SourcePath = "first.txt" });
            using var dialog = new ComponentFilesDialog(component, null);
            dialog.ReadSizes = (paths, _, _) =>
            {
                Environment.CurrentManagedThreadId.Should().NotBe(thread);
                if (paths[0] == "first.txt") { started.Set(); release.Wait(); return new(1024 * 1024, 0); }
                return new(2 * 1024 * 1024, 0);
            };
            dialog.Show(); var old = dialog.PendingSize;
            try
            {
                UiStaTest.Pump(Task.Run(() => started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue()));
                var field = dialog.Controls.Find("ComponentFileSource", true).OfType<TextBox>().Single();
                field.Text = "second.txt"; UiStaTest.Pump(dialog.PendingSize);
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    dialog.Controls.Find("FileSizeSummary", true).OfType<Label>().Single().Text.Should().Be(
                        string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Files_TotalSize"), 2.0, 1, 0));
                }
                release.Set(); UiStaTest.Pump(old);
                dialog.Controls.Find("FileSizeSummary", true).OfType<Label>().Single().Text.Should().Be(
                    string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Files_TotalSize"), 2.0, 1, 0));
                component.Files[0].SourcePath.Should().Be("first.txt");
            }
            finally { release.Set(); UiStaTest.Pump(old); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("edit")]
    [InlineData("unnotified_edit")]
    [InlineData("dispose")]
    [InlineData("failure")]
    [InlineData("newer")]
    public void SlowDropPreservesDraftOnCancellationEditDisposalFailureOrSupersession(string kind)
    {
        Sta(() =>
        {
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var component = new InstallComponent(); component.Files.Add(new FileCopyOperation { SourcePath = "original.txt", Description = "Original" });
            using var dialog = new ComponentFilesDialog(component, null); dialog.Show(); UiStaTest.Pump(dialog.PendingSize);
            var canonical = JsonSerializer.Serialize(component);
            dialog.ReadDrop = (paths, _, _, _) =>
            {
                if (paths[0] == "first") { started.Set(); release.Wait(); }
                if (kind == "failure") throw new IOException("Owned read failure");
                return new(new[] { new FileCopyOperation { SourcePath = paths[0] + ".txt", DestinationPath = paths[0] + ".txt" } }, 0, 0, 0, Array.Empty<string>());
            };
            dialog.BeginDrop(new[] { "first" }); var pending = dialog.PendingDrop;
            try
            {
                UiStaTest.Pump(Task.Run(() => started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue()));
                dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().Enabled.Should().BeFalse();
                var status = dialog.Controls.Find("ComponentFileDropStatus", true).OfType<Label>().Single();
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    status.Text.Should().Be(LanguageManager.GetString("Files_DropScanning"));
                    status.AccessibleName.Should().Be(status.Text);
                }
                if (kind == "cancel") dialog.Controls.Find("Files_CancelScan", true).OfType<Button>().Single().PerformClick();
                if (kind == "edit") dialog.Controls.Find("ComponentFileDescription", true).OfType<TextBox>().Single().Text = "Typed draft edit";
                if (kind == "unnotified_edit") dialog.ResultFiles[0].SharedCount = true;
                if (kind == "dispose") dialog.Dispose();
                if (kind == "newer") { dialog.BeginDrop(new[] { "second" }); UiStaTest.Pump(dialog.PendingDrop); }
                var before = JsonSerializer.Serialize(dialog.ResultFiles);
                release.Set(); UiStaTest.Pump(pending); UiStaTest.Pump(dialog.PendingSize);
                JsonSerializer.Serialize(dialog.ResultFiles).Should().Be(before);
                JsonSerializer.Serialize(component).Should().Be(canonical);
                if (kind == "dispose") status.IsDisposed.Should().BeTrue();
                else
                {
                    var key = kind switch { "cancel" => "Files_DropCancelled", "failure" => "Files_DropFailed",
                        "newer" => "Files_DropSummary", _ => "Files_DropStale" };
                    if (kind == "newer") dialog.ResultFiles.Should().Contain(file => file.SourcePath == "second.txt");
                    foreach (var culture in LanguageManager.SupportedCultures)
                    {
                        LanguageManager.SetLanguage(culture); Application.DoEvents();
                        status.Text.Should().Contain(kind == "newer" ? string.Format(LanguageManager.CurrentCulture,
                            LanguageManager.GetString(key), 1, 0, 0, 0) : LanguageManager.GetString(key));
                        status.AccessibleName.Should().Be(status.Text);
                    }
                    dialog.Controls.Find("ComponentFilesOk", true).OfType<Button>().Single().Enabled.Should().BeTrue();
                }
            }
            finally { release.Set(); UiStaTest.Pump(pending); }
        });
    }

    [Fact]
    public void MetadataOnlyScannerKeepsDiscoveryRulesWithoutConfiguringDependenciesAndHonorsCancellation()
    {
        WithFixture((root, folder, first, nested, conflict) =>
        {
            var result = new SourceScanner().ScanFileMetadata(folder);
            result.Files.Select(file => file.FullPath).Should().BeEquivalentTo(new[] { first, nested });
            result.DependencyPaths.Should().BeEmpty(); result.SuggestedPrerequisites.Should().BeEmpty();
            result.Warnings.Should().BeEmpty();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Action scan = () => new SourceScanner().ScanFileMetadata(root, cancellationToken: cancellation.Token);
            scan.Should().Throw<OperationCanceledException>();
        });
    }

    private static void Raise(Control control, string name, DragEventArgs args) => typeof(Control)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { args });

    private static void WithFixture(Action<string, string, string, string, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepDrop_" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "assets"); var nestedFolder = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nestedFolder);
        var first = Path.Combine(folder, "main.txt"); var nested = Path.Combine(nestedFolder, "child.txt"); var conflict = Path.Combine(root, "conflict.txt");
        File.WriteAllText(first, "owned first"); File.WriteAllText(nested, "owned nested"); File.WriteAllText(conflict, "owned conflict");
        try { action(root, folder, first, nested, conflict); }
        finally
        {
            foreach (var file in new[] { first, nested, conflict }) File.Delete(file);
            Directory.Delete(nestedFolder); Directory.Delete(folder); Directory.Delete(root);
        }
    }

    [Fact]
    public void AdvertisedFileDropIsEnabledOnTheActualGrid()
    {
        Sta(() =>
        {
            using var dialog = new ComponentFilesDialog(new InstallComponent(), null); dialog.Show();
            UiStaTest.Pump(dialog.PendingSize);
            dialog.Controls.Find("ComponentFileGrid", true).OfType<DataGridView>().Single().AllowDrop.Should().BeTrue();
        });
    }

    [Fact]
    public void EditingSourcePathRefreshesItsSizeWithoutChangingTheComponent()
    {
        Sta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepFileSize_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var first = Path.Combine(root, "first.txt"); var second = Path.Combine(root, "second.txt");
            File.WriteAllBytes(first, new byte[1024 * 1024]); File.WriteAllBytes(second, new byte[2 * 1024 * 1024]);
            try
            {
                var component = new InstallComponent(); component.Files.Add(new FileCopyOperation { SourcePath = first });
                using var dialog = new ComponentFilesDialog(component, root); dialog.Show();
                UiStaTest.Pump(dialog.PendingSize);
                dialog.Controls.Find("ComponentFileSource", true).OfType<TextBox>().Single().Text = second;
                UiStaTest.Pump(dialog.PendingSize);
                var status = dialog.Controls.Find("FileSizeSummary", true).OfType<Label>().Single();
                status.Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Files_TotalSize"), 2.0, 1, 0));
                component.Files[0].SourcePath.Should().Be(first);
            }
            finally { File.Delete(first); File.Delete(second); Directory.Delete(root); }
        });
    }

    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });
}
