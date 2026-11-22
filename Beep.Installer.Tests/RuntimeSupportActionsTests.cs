using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Pages;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class RuntimeSupportActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepSupportActions-" + Guid.NewGuid().ToString("N"));
    public RuntimeSupportActionsTests() => Directory.CreateDirectory(_root);
    private string FileAt(string name) => Path.Combine(_root, name);
    public void Dispose() { foreach (var file in Directory.EnumerateFiles(_root)) File.Delete(file); Directory.Delete(_root); }
    private string Capture()
    {
        var project = InstallerProjectFactory.CreateNew("Support fixture", "1.0.0", "Publisher", "");
        return new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
        {
            Project = project, Action = "install", Success = false, ExitCode = 1, Message = "Provider failed password=PRIVATE_VALUE",
            Outcome = new RuntimeOperationOutcome(false, true, true, 3, 2, BackupRecoveryState.Failed),
            PreviewOnly = true, ConsentGranted = false, RetentionDays = 0, OutputPath = FileAt("captured-support-bundle.json")
        }).Path;
    }
    private static void Sta(Action action)
    {
        UiStaTest.Run(() =>
        {
            var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            finally { LanguageManager.SetLanguage(language); }
        });
    }
    private static void Pump(Task task)
    {
        var started = DateTime.UtcNow;
        while (!task.IsCompleted && DateTime.UtcNow - started < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(10); }
        task.IsCompleted.Should().BeTrue(); task.GetAwaiter().GetResult();
    }
    private static TextBox Body(RuntimeSupportBundleDialog dialog) => dialog.Controls.Find("SupportBundlePreview", true).OfType<TextBox>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Fact]
    public void SupportCaptionsArePackagedWithMatchingFormatArgumentsInAllLanguages()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Support_", StringComparison.Ordinal))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
        var english = Read("en"); english.Should().HaveCount(10);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var key in english.Keys)
            {
                translated[key].Should().NotBeNullOrWhiteSpace();
                System.Text.CompositeFormat.Parse(translated[key]).MinimumArgumentCount.Should().Be(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount);
            }
        }
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void ReviewAndExportActionsFitAtMinimumSizeAndSupportedScales(string culture, float scale)
    {
        var captured = Capture();
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture); using var dialog = new RuntimeSupportBundleDialog(captured);
            dialog.Size = dialog.MinimumSize; dialog.Show(); Pump(dialog.LoadAsync());
            dialog.Scale(new SizeF(scale, scale)); dialog.PerformLayout(); Application.DoEvents();
            foreach (var control in Descendants(dialog).Where(control => control.Visible && control is Button or TextBox))
            {
                var actual = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                dialog.ClientRectangle.Contains(actual).Should().BeTrue($"{culture}/{scale} {control.AccessibleName}: {actual} inside {dialog.ClientRectangle}");
            }
            Body(dialog).Height.Should().BeGreaterThan(80); Body(dialog).ReadOnly.Should().BeTrue();
            Body(dialog).RightToLeft.Should().Be(culture == "ar" ? RightToLeft.Yes : RightToLeft.No);
            dialog.ChooseExportPath = _ => null;
            ((Button)dialog.Controls.Find("SupportBundleExport", true).Single()).PerformClick();
            Directory.EnumerateFiles(_root).Should().ContainSingle();
        });
    }

    [Fact]
    public void BothTerminalPageActionsOpenTheReviewDialogWithoutReplacingRecoveryEvidence()
    {
        var captured = Capture();
        Sta(() =>
        {
            foreach (var success in new[] { true, false })
            {
                using var host = new Form(); using UserControl page = success ? new CompletePage() : new ErrorPage();
                page.Dock = DockStyle.Fill; host.Controls.Add(page); host.Show();
                if (page is CompletePage complete) complete.SetResult(true, "completed evidence", supportBundlePath: captured);
                if (page is ErrorPage error) error.SetError("original failure", supportBundlePath: captured, outcome: new RuntimeOperationOutcome(false, false, true, 3, 2, BackupRecoveryState.Failed));
                var action = Descendants(page).Single(control => control.Text == LanguageManager.GetString("Support_Review"));
                bool seen = false; Exception? failure = null; Task? pendingRead = null;
                using var timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<RuntimeSupportBundleDialog>().SingleOrDefault(form => !form.InvokeRequired && form.Modal);
                    if (dialog?.PendingLoad is null) return;
                    pendingRead = dialog.PendingLoad;
                    timer.Stop(); seen = true;
                    try { dialog.Text.Should().Be(LanguageManager.GetString("Support_Review")); }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start(); typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(action, new object[] { EventArgs.Empty });
                pendingRead.Should().NotBeNull(); Pump(pendingRead!);
                seen.Should().BeTrue(); if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                if (page is ErrorPage failed) failed.GetSupportDetails().Should().Contain("original failure").And.Contain("file backup failed");
            }
        });
    }

    [Fact]
    public void ExportUsesTheReviewedSnapshotAndPreservesFailureRecoveryAndPrivacyEvidence()
    {
        var captured = Capture(); var preview = RuntimeSupportBundleExport.ReadPreview(captured); preview.CanExport.Should().BeTrue();
        preview.Json.Should().NotContain("PRIVATE_VALUE");
        File.WriteAllText(captured, "changed after review");
        var destination = FileAt("export.json"); RuntimeSupportBundleExport.Export(preview, destination);
        File.ReadAllText(destination).Should().Be(preview.Json); File.ReadAllText(captured).Should().Be("changed after review");
        using var doc = JsonDocument.Parse(File.ReadAllText(destination));
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("operationOutcome").GetProperty("backupRecovery").GetString().Should().Be("Failed");
        doc.RootElement.GetProperty("operationOutcome").GetProperty("fullRecoveryVerified").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("privacy").GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("privacy").GetProperty("consentGranted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ReRedactionHonorsDeclaredProviderInputsAndLocalPaths()
    {
        var captured = Capture(); var doc = JsonNode.Parse(File.ReadAllText(captured))!.AsObject();
        doc["fixtureProvider"] = new JsonObject { ["inputs"] = new JsonObject { ["Opaque"] = "DECLARED_PRIVATE", ["public"] = "keep" }, ["sensitiveInputs"] = new JsonArray("opaque") };
        doc["fixtureInfo"] = new JsonObject { ["privateKey"] = "PRIVATE_KEY", ["text"] = "token=PRIVATE_TOKEN " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        File.WriteAllText(captured, doc.ToJsonString());
        var preview = RuntimeSupportBundleExport.ReadPreview(captured); preview.CanExport.Should().BeTrue();
        preview.Json.Should().NotContain("DECLARED_PRIVATE").And.NotContain("PRIVATE_KEY").And.NotContain("PRIVATE_TOKEN").And.Contain("keep").And.Contain("%USERPROFILE%");
    }

    [Theory]
    [InlineData("missing")] [InlineData("malformed")] [InlineData("duplicate")] [InlineData("schema")]
    [InlineData("privacy")] [InlineData("outcome")] [InlineData("oversize")]
    public void InvalidArtifactsCannotCreateAnExport(string problem)
    {
        var captured = Capture(); var doc = JsonNode.Parse(File.ReadAllText(captured))!.AsObject();
        switch (problem)
        {
            case "missing": File.Delete(captured); break;
            case "malformed": File.WriteAllText(captured, "PRIVATE_INVALID_JSON"); break;
            case "duplicate": File.WriteAllText(captured, File.ReadAllText(captured).Replace("\"success\": false", "\"success\": false, \"Success\": true")); break;
            case "schema": doc["schemaVersion"] = "99"; File.WriteAllText(captured, doc.ToJsonString()); break;
            case "privacy": doc["privacy"]!["redactionApplied"] = false; File.WriteAllText(captured, doc.ToJsonString()); break;
            case "outcome": doc["success"] = true; File.WriteAllText(captured, doc.ToJsonString()); break;
            case "oversize": using (var stream = File.Create(captured)) stream.SetLength(RuntimeSupportBundleExport.MaximumBytes + 1L); break;
        }
        var preview = RuntimeSupportBundleExport.ReadPreview(captured); preview.CanExport.Should().BeFalse(); preview.Json.Should().BeEmpty();
        var destination = FileAt("invalid-export.json"); Action export = () => RuntimeSupportBundleExport.Export(preview, destination);
        export.Should().Throw<InvalidOperationException>(); File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void ExportRejectsSourceOverwriteAndPreservesExistingFilesOnWriteFailure()
    {
        var captured = Capture(); var original = File.ReadAllText(captured); var preview = RuntimeSupportBundleExport.ReadPreview(captured);
        Action overwrite = () => RuntimeSupportBundleExport.Export(preview, captured); overwrite.Should().Throw<InvalidOperationException>();
        File.ReadAllText(captured).Should().Be(original);
        var destination = FileAt("locked.json"); File.WriteAllText(destination, "previous export");
        using (var held = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        { Action locked = () => RuntimeSupportBundleExport.Export(preview, destination); locked.Should().Throw<Exception>().Which.Should().Match<Exception>(ex => ex is IOException || ex is UnauthorizedAccessException); }
        File.ReadAllText(destination).Should().Be("previous export"); Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("password=PRIVATE")] [InlineData("token='PRIVATE SECRET'")] [InlineData("api_key=\"PRIVATE SECRET\"")]
    public void CopyUsesTheSharedRedactorAndKeepsRecoveryFailure(string detail)
    {
        Sta(() =>
        {
            using var page = new ErrorPage(); page.SetError(detail, outcome: new RuntimeOperationOutcome(false, true, true, 2, 1, BackupRecoveryState.Failed));
            string? copied = null; page.SupportDetailsCopy = text => copied = text;
            var button = page.Controls.Find("RuntimeCopyErrorDetails", true).Single();
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
            copied.Should().NotBeNull().And.NotContain("PRIVATE").And.Contain("file backup failed").And.Contain("not verified").And.Contain("Restart Windows");
            page.GetSupportDetails().Should().Be(copied);
            page.CopySupportDetails(_ => throw new System.Runtime.InteropServices.ExternalException()).Should().BeFalse();
            page.Controls.OfType<TableLayoutPanel>().Single().Controls.Cast<Control>().Should().NotBeEmpty();
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void NativeReviewExportsCapturedEvidenceAndPreservesItAcrossLanguageAndWriteFailure(int cycle)
    {
        var captured = Capture();
        Sta(() =>
        {
            using var dialog = new RuntimeSupportBundleDialog(captured); dialog.Show(); Pump(dialog.LoadAsync());
            Body(dialog).ReadOnly.Should().BeTrue(); Body(dialog).Text.Should().NotContain("PRIVATE_VALUE");
            var before = Body(dialog).Text;
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                Body(dialog).Text.Should().Be(before, $"culture {culture}, independent window cycle {cycle}");
                Body(dialog).RightToLeft.Should().Be(LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No);
                dialog.LoadAsync().Should().BeSameAs(dialog.PendingLoad);
            }
            var failed = dialog.ExportToAsync(captured); Pump(failed); failed.Result.Should().BeFalse(); Body(dialog).Text.Should().Be(before);
            var path = FileAt("reviewed.json"); dialog.ChooseExportPath = _ => path;
            var exportButton = (Button)dialog.Controls.Find("SupportBundleExport", true).Single();
            exportButton.Enabled.Should().BeTrue();
            exportButton.CanSelect.Should().BeTrue($"the reviewed action remains selectable; visible={exportButton.Visible}, ownerVisible={dialog.Visible}, ownerEnabled={dialog.Enabled}, activeOwner={Form.ActiveForm == dialog}");
            exportButton.PerformClick(); dialog.PendingExport.Should().NotBeNull("the export click must start an observable operation");
            Pump(dialog.PendingExport!); File.ReadAllText(path).Should().Be(before);
            dialog.Dispose(); Action language = () => LanguageManager.SetLanguage("de"); language.Should().NotThrow();
        });
    }

    [Fact]
    public void ConcurrentLoadRequestsCaptureOnceAndNeverReplaceReviewedEvidenceAfterSourceChanges()
    {
        var captured = Capture();
        Sta(() =>
        {
            using var release = new ManualResetEventSlim(); using var started = new ManualResetEventSlim();
            var reads = 0;
            using var dialog = new RuntimeSupportBundleDialog(captured)
            {
                ReadPreview = path =>
                {
                    Interlocked.Increment(ref reads); started.Set(); release.Wait(TimeSpan.FromSeconds(15));
                    return RuntimeSupportBundleExport.ReadPreview(path);
                }
            };
            dialog.Show(); Application.DoEvents(); var first = dialog.LoadAsync(); var second = dialog.LoadAsync();
            try
            {
                started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                first.Should().BeSameAs(second, "an open review owns one captured read, including its Shown request");
                first.Should().BeSameAs(dialog.PendingLoad);
            }
            finally { release.Set(); Pump(Task.WhenAll(first, second, dialog.PendingLoad!)); }
            reads.Should().Be(1);
            var before = Body(dialog).Text;
            File.WriteAllText(captured, "changed after review");
            LanguageManager.SetLanguage("ar"); Application.DoEvents();
            var later = dialog.LoadAsync(); Pump(later); later.Should().BeSameAs(first);
            reads.Should().Be(1); Body(dialog).Text.Should().Be(before);
            dialog.Controls.Find("SupportBundleExport", true).Single().Enabled.Should().BeTrue();
            var destination = FileAt("retained.json"); var export = dialog.ExportToAsync(destination); Pump(export);
            export.Result.Should().BeTrue(); File.ReadAllText(destination).Should().Be(before);
            File.ReadAllText(captured).Should().Be("changed after review");
        });
    }

    [Fact]
    public void DisposalDuringTheFirstHeldReadCannotPublishOrStartAnotherRead()
    {
        var captured = Capture();
        Sta(() =>
        {
            using var release = new ManualResetEventSlim(); using var started = new ManualResetEventSlim();
            var reads = 0;
            using var dialog = new RuntimeSupportBundleDialog(captured)
            {
                ReadPreview = path =>
                {
                    Interlocked.Increment(ref reads); started.Set(); release.Wait(TimeSpan.FromSeconds(15));
                    return RuntimeSupportBundleExport.ReadPreview(path);
                }
            };
            dialog.Show(); Application.DoEvents(); var pending = dialog.LoadAsync();
            try
            {
                started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(); pending.IsCompleted.Should().BeFalse();
                dialog.Dispose(); dialog.LoadAsync().Should().BeSameAs(pending);
            }
            finally { release.Set(); Pump(pending); }
            reads.Should().Be(1);
            typeof(RuntimeSupportBundleDialog).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog).Should().BeNull();
            File.Delete(captured); File.Exists(captured).Should().BeFalse("the completed owned read released its source handle");
        });
    }

    [Fact]
    public void UnavailableReviewDisablesExportAndDisposalDuringReadCannotPublish()
    {
        Sta(() =>
        {
            using var dialog = new RuntimeSupportBundleDialog(FileAt("missing.json")); dialog.Show(); Pump(dialog.LoadAsync());
            dialog.Controls.Find("SupportBundleExport", true).Single().Enabled.Should().BeFalse();
            var rejected = dialog.ExportToAsync(FileAt("no-output.json")); Pump(rejected); rejected.Result.Should().BeFalse(); File.Exists(FileAt("no-output.json")).Should().BeFalse();
            var pending = dialog.LoadAsync(); dialog.Dispose(); Pump(pending);
        });
    }

    [Fact]
    public void DisposalDuringExportKeepsTheAuthorizedSnapshotWriteAndDoesNotPublishToTheClosedDialog()
    {
        var captured = Capture();
        Sta(() =>
        {
            using var dialog = new RuntimeSupportBundleDialog(captured); dialog.Show(); Pump(dialog.LoadAsync());
            var reviewed = Body(dialog).Text; var path = FileAt("closed-dialog-export.json");
            var save = dialog.ExportToAsync(path); dialog.Dispose(); Pump(save); save.Result.Should().BeTrue();
            File.ReadAllText(path).Should().Be(reviewed);
        });
    }
}
