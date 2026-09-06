using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AccessibilityLocalizationQualificationRunnerTests : IDisposable
{
    private readonly string _root;

    public AccessibilityLocalizationQualificationRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BeepA11y_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Run_WritesEnterpriseQualificationReport_ForAccessibleLocalizedSource()
    {
        var sourceRoot = CreateQualifiedSourceRoot();
        var output = Path.Combine(_root, "out");

        var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
        {
            SourceRoot = sourceRoot,
            OutputDirectory = output
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        File.Exists(report.ReportPath).Should().BeTrue();
        File.Exists(report.ManualEvidenceChecklistPath).Should().BeTrue();
        File.Exists(Path.Combine(output, AccessibilityLocalizationQualificationRunner.ManualEvidenceManifestFileName)).Should().BeTrue();
        File.ReadAllText(report.ManualEvidenceChecklistPath).Should().Contain("accessibility-insights");
        report.Scenarios.Select(s => s.Id).Should().BeEquivalentTo(new[]
        {
            "resource-key-parity",
            "arabic-rtl-readiness",
            "dpi-autoscale-readiness",
            "keyboard-screen-reader-readiness",
            "high-contrast-readiness",
            "cli-locale-invariance",
            "manual-walkthrough-evidence"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
    }

    [Fact]
    public void Run_FailsClosed_WhenCultureKeySetsDrift()
    {
        var sourceRoot = CreateQualifiedSourceRoot();
        File.WriteAllText(Path.Combine(sourceRoot, "Beep.Installer", "Lang", "Strings_ar.resx"), Resx(("Btn_Next", "التالي")));

        var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
        {
            SourceRoot = sourceRoot,
            OutputDirectory = Path.Combine(_root, "out-drift")
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "resource-key-parity").Diagnostics
            .Should().Contain(d => d.Code == "BI2703" && d.Path.Contains("Btn_Cancel", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_RequiresManualEvidenceArtifacts_WhenGateIsEnabled()
    {
        var sourceRoot = CreateQualifiedSourceRoot();
        var evidence = Path.Combine(_root, "manual-evidence");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "narrator.txt"), "Narrator walkthrough");

        var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
        {
            SourceRoot = sourceRoot,
            OutputDirectory = Path.Combine(_root, "out-manual"),
            ManualEvidenceDirectory = evidence,
            RequireManualEvidence = true
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "manual-walkthrough-evidence").Diagnostics
            .Should().Contain(d => d.Code == "BI2761" && d.Path.Contains("keyboard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_PassesManualEvidenceGate_WhenRequiredArtifactsArePresent()
    {
        var sourceRoot = CreateQualifiedSourceRoot();
        var evidence = Path.Combine(_root, "complete-manual-evidence");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "narrator.md"), "Narrator walkthrough");
        File.WriteAllText(Path.Combine(evidence, "keyboard.md"), "Keyboard walkthrough");
        File.WriteAllText(Path.Combine(evidence, "accessibility-insights.html"), "<html>Accessibility Insights report</html>");
        File.WriteAllText(Path.Combine(evidence, "arabic-100.png"), "screenshot-100");
        File.WriteAllText(Path.Combine(evidence, "arabic-150.png"), "screenshot-150");
        File.WriteAllText(Path.Combine(evidence, "arabic-200.png"), "screenshot-200");

        var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
        {
            SourceRoot = sourceRoot,
            OutputDirectory = Path.Combine(_root, "out-complete-manual"),
            ManualEvidenceDirectory = evidence,
            RequireManualEvidence = true
        });

        report.Success.Should().BeTrue();
        var manual = report.Scenarios.Single(s => s.Id == "manual-walkthrough-evidence");
        manual.Success.Should().BeTrue();
        manual.EvidenceFiles.Should().HaveCount(6);
        manual.EvidenceFiles.Should().Contain(file =>
            file.RequirementId == "accessibility-insights"
            && file.Extension == ".html"
            && file.SizeBytes > 0);
    }

    [Fact]
    public void Run_FailsManualEvidenceGate_ForEmptyOrWrongTypeArtifacts()
    {
        var sourceRoot = CreateQualifiedSourceRoot();
        var evidence = Path.Combine(_root, "bad-manual-evidence");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "narrator.md"), "Narrator walkthrough");
        File.WriteAllText(Path.Combine(evidence, "keyboard.md"), "Keyboard walkthrough");
        File.WriteAllText(Path.Combine(evidence, "accessibility-insights.txt"), "wrong extension");
        File.WriteAllText(Path.Combine(evidence, "arabic-100.png"), "screenshot-100");
        File.WriteAllText(Path.Combine(evidence, "arabic-150.png"), "");
        File.WriteAllText(Path.Combine(evidence, "arabic-200.png"), "screenshot-200");

        var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
        {
            SourceRoot = sourceRoot,
            OutputDirectory = Path.Combine(_root, "out-bad-manual"),
            ManualEvidenceDirectory = evidence,
            RequireManualEvidence = true
        });

        report.Success.Should().BeFalse();
        var diagnostics = report.Scenarios.Single(s => s.Id == "manual-walkthrough-evidence").Diagnostics;
        diagnostics.Should().Contain(d => d.Code == "BI2762" && d.Path.EndsWith("accessibility-insights", StringComparison.Ordinal));
        diagnostics.Should().Contain(d => d.Code == "BI2763" && d.Path.EndsWith("arabic-150", StringComparison.Ordinal));
    }

    private string CreateQualifiedSourceRoot()
    {
        var sourceRoot = Path.Combine(_root, "src");
        var lang = Path.Combine(sourceRoot, "Beep.Installer", "Lang");
        var engine = Path.Combine(sourceRoot, "Beep.Installer", "Engine");
        var forms = Path.Combine(sourceRoot, "Beep.Installer", "Forms");
        var core = Path.Combine(sourceRoot, "Beep.Installer.Core", "Runtime");
        Directory.CreateDirectory(lang);
        Directory.CreateDirectory(engine);
        Directory.CreateDirectory(forms);
        Directory.CreateDirectory(core);

        foreach (var culture in new[] { "en", "ar", "es", "fr", "de", "zh", "ja", "pt" })
            File.WriteAllText(Path.Combine(lang, $"Strings_{culture}.resx"), Resx(("Btn_Next", culture + " next"), ("Btn_Cancel", culture + " cancel")));

        File.WriteAllText(Path.Combine(engine, "RtlHelper.cs"), """
            using System.Windows.Forms;
            public static class RtlHelper
            {
                public static bool IsRtl(string c) => c == "ar";
                public static void ApplyRtl(Form form) { form.RightToLeftLayout = true; }
            }
            """);
        File.WriteAllText(Path.Combine(engine, "Accessibility.cs"), """
            using System.Windows.Forms;
            using System.Drawing;
            public static class Accessibility
            {
                public static void ApplyAutoNames(Control c) { c.AccessibleName = c.Text; }
                public static void NormalizeTabOrder(Control c) { c.TabStop = true; }
                public static void EnsureAccessibility(Form form) { if (SystemInformation.HighContrast) { form.ForeColor = SystemColors.WindowText; form.BackColor = SystemColors.ControlText; } }
            }
            """);
        File.WriteAllText(Path.Combine(forms, "BeepModernInstallerForm.cs"), """
            using System.Windows.Forms;
            public sealed class BeepModernInstallerForm : Form
            {
                public BeepModernInstallerForm()
                {
                    AutoScaleMode = AutoScaleMode.Dpi;
                    Engine.Accessibility.EnsureAccessibility(this);
                    if (Engine.RtlHelper.IsRtl("ar")) Engine.RtlHelper.ApplyRtl(this);
                    Controls.Add(new Button { AccessibleName = "Next" });
                }
            }
            """);
        File.WriteAllText(Path.Combine(core, "CliContract.cs"), """
            using System.Globalization;
            public static class CliContract { public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture); }
            """);

        return sourceRoot;
    }

    private static string Resx(params (string Key, string Value)[] entries)
    {
        var data = string.Join(Environment.NewLine, entries.Select(e => $"""
              <data name="{e.Key}" xml:space="preserve"><value>{e.Value}</value></data>
            """));
        return $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>
            {{data}}
            </root>
            """;
    }
}
