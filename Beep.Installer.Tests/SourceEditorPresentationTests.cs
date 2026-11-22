using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class SourceEditorPresentationTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Field<T>(object root, string name) => (T)root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
    private static void Invoke(object root, string name, params object[] args) => root.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, args);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(original); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearedOrInvalidSourceDiscardsAnAlreadyQueuedFileTreeResult(bool invalid)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BeepTreeRevision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Sta(() =>
            {
                var controller = new InstallerController(); controller.StopAutoSave();
                using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("source");
                var tree = Find<TreeView>(editor.ActiveSection!, "SourceFiles");
                var originalContext = SynchronizationContext.Current;
                using var queued = new HeldUiContext();
                try
                {
                    SynchronizationContext.SetSynchronizationContext(queued);
                    controller.Project.SourceDirectory = directory;
                    queued.Posted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the old source tree has completed and is waiting for its UI continuation");
                    controller.Project.SourceDirectory = invalid ? Path.Combine(directory, "missing") : "";
                }
                finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
                var before = InstallerScriptSerializer.Write(controller.Project);
                queued.Drain(); Application.DoEvents();
                tree.Nodes.Count.Should().Be(0, "a queued result for the old valid source must not repopulate the cleared tree");
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            });
        }
        finally { Directory.Delete(directory); }
    }

    private sealed class HeldUiContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        internal ManualResetEventSlim Posted { get; } = new();
        public override void Post(SendOrPostCallback callback, object? state) { _callbacks.Enqueue((callback, state)); Posted.Set(); }
        internal void Drain() { while (_callbacks.TryDequeue(out var work)) work.Callback(work.State); }
        public void Dispose() => Posted.Dispose();
    }

    [Fact]
    public void PatternDraftsFocusSelectionsAndCollectionsSurviveEveryLanguageAndActionsRefreshTheList()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            controller.Project.SourceIncludes.Add("*.exe"); controller.Project.SourceExcludes.Add("*.tmp");
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.Size = editor.MinimumSize; editor.OpenSection("includes");
            var panel = editor.ActiveSection!;
            var draft = Find<TextBox>(panel, "SourceIncludesDraft"); var exclude = Find<TextBox>(panel, "SourceExcludesDraft");
            var list = Find<ListBox>(panel, "SourceIncludes"); list.SelectedItem = "*.exe";
            draft.Text = "  *.dll  "; exclude.Text = "pending exclusion"; draft.Focus(); draft.Select(2, 5);
            var before = InstallerScriptSerializer.Write(controller.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                editor.ActiveSection.Should().BeSameAs(panel); draft.Text.Should().Be("  *.dll  "); exclude.Text.Should().Be("pending exclusion");
                draft.Focused.Should().BeTrue(); draft.SelectionStart.Should().Be(2); draft.SelectionLength.Should().Be(5); list.SelectedItem.Should().Be("*.exe");
                foreach (var (name, key) in new[] { ("SourceIncludes", "Builder_IncludePatterns"), ("SourceExcludes", "Builder_ExcludePatterns") })
                {
                    Find<Label>(panel, name + "Caption").Text.Should().Be(LanguageManager.GetString(key));
                    Find<TextBox>(panel, name + "Draft").AccessibleName.Should().Be(LanguageManager.GetString(key).TrimEnd(':'));
                    Find<Button>(panel, name + "Add").Text.Should().Be(LanguageManager.GetString("Btn_Add"));
                    Find<Button>(panel, name + "Remove").Text.Should().Be(LanguageManager.GetString("Btn_Remove"));
                }
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            Find<Button>(panel, "SourceIncludesAdd").PerformClick();
            controller.Project.SourceIncludes.Should().Contain("*.dll"); list.Items.Cast<string>().Should().Contain("*.dll"); draft.Text.Should().BeEmpty();
            list.SelectedItem = "*.dll"; Find<Button>(panel, "SourceIncludesRemove").PerformClick();
            controller.Project.SourceIncludes.Should().NotContain("*.dll"); list.Items.Cast<string>().Should().NotContain("*.dll");
        });
    }
    [Fact]
    public void ComponentActionsHeadersAndArrowNamesTranslateWithoutReplacingSelectedResources()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            controller.Project.Components.Add(new() { Id = "first", Name = "First" }); controller.Project.Components.Add(new() { Id = "second", Name = "Second" });
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("components"); var panel = editor.ActiveSection!;
            var grid = Descendants(panel).OfType<DataGridView>().Single(); var binding = (BindingSource)grid.DataSource!; binding.Position = 1;
            var selected = binding.Current; var before = InstallerScriptSerializer.Write(controller.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                binding.Current.Should().BeSameAs(selected); editor.ActiveSection.Should().BeSameAs(panel);
                foreach (var name in new[] { "Id", "Name", "Description", "Required", "Selected" }) grid.Columns[name]!.HeaderText.Should().Be(LanguageManager.GetString("Grid_" + name));
                foreach (var key in new[] { "Btn_Add", "Builder_Duplicate", "Builder_EditFiles", "Btn_Remove", "Builder_ScanSource" })
                    Descendants(panel).OfType<Button>().Should().Contain(button => button.Text == LanguageManager.GetString(key) && button.AccessibleName == button.Text);
                foreach (var (glyph, key) in new[] { ("▲", "Builder_MoveUp"), ("▼", "Builder_MoveDown") })
                    Descendants(panel).OfType<Button>().Single(button => button.Text == glyph).AccessibleName.Should().Be(LanguageManager.GetString(key));
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
        });
    }
    [Fact]
    public void RealScanCaptionsTranslateWhileTreePathsAndSelectionsRemainAuthoredAndOtherStatusIsRespected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BeepSourceCaption_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "fixture.txt"); File.WriteAllText(file, "fixture");
        try
        {
            Sta(() =>
            {
                var controller = new InstallerController(); controller.StopAutoSave();
                using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("source"); var panel = editor.ActiveSection!;
                Find<Label>(panel, "SourceScanStatus").Text.Should().Be(LanguageManager.GetString("Builder_NoScan"));
                Invoke(editor, "ScanAndPopulate", directory);
                var watch = Stopwatch.StartNew(); while (Field<bool>(editor, "_scanInProgress") && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(10); }
                Field<bool>(editor, "_scanInProgress").Should().BeFalse(); Application.DoEvents();
                var path = Find<TextBox>(panel, "SourceDirectory"); path.Focus(); path.Select(2, 7);
                var before = InstallerScriptSerializer.Write(controller.Project);
                var tree = Find<TreeView>(panel, "SourceFiles"); var nodes = tree.Nodes.Cast<TreeNode>().Select(node => (node.Text, node.Tag)).ToArray();
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    var expected = string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Builder_ScanComplete"), 1, new FileInfo(file).Length / 1024d / 1024d, 0);
                    Find<Label>(panel, "SourceScanStatus").Text.Should().Be(expected); Field<ToolStripStatusLabel>(editor, "_statusLabel").Text.Should().Be(expected);
                    Find<Button>(panel, "RescanSource").Text.Should().Be(LanguageManager.GetString("Btn_Rescan"));
                    Find<Label>(panel, "SourceDirectoryCaption").Text.Should().Be(LanguageManager.GetString("Builder_SourceDir"));
                    path.Text.Should().Be(directory); path.Focused.Should().BeTrue(); path.SelectionStart.Should().Be(2); path.SelectionLength.Should().Be(7);
                    tree.Nodes.Cast<TreeNode>().Select(node => (node.Text, node.Tag)).Should().Equal(nodes); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                }
                Invoke(editor, "SetStatus", "newer unrelated action"); LanguageManager.SetLanguage("en");
                Field<ToolStripStatusLabel>(editor, "_statusLabel").Text.Should().Be("newer unrelated action");
                controller.Project.SourceDirectory = "";
                Find<Label>(panel, "SourceScanStatus").Text.Should().Be(LanguageManager.GetString("Builder_NoScan"));
            });
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }
}
