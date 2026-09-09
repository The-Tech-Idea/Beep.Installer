using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Pages;
using FluentAssertions;
using Xunit;
using A11y = Beep.Installer.Engine.Accessibility;
using Rtl = Beep.Installer.Engine.RtlHelper;

namespace Beep.Installer.Tests;

/// <summary>
/// The mechanical half of the Arabic visual pass (7.B.2) and of an Accessibility Insights run
/// (7.M.1).
///
/// Neither can be fully automated — whether Arabic *reads* well, and what Narrator actually says out
/// loud, need a person. But most of what those passes look for is measurable: controls that did not
/// mirror, text that clips once mirrored, missing accessible names, unreachable tab stops, and
/// contrast below the WCAG AA threshold. Automating that leaves the human with the genuinely
/// subjective residue instead of a checklist they have to grind through first.
/// </summary>
[Collection("Language")]
public class RtlAndAccessibilityAuditTests : IDisposable
{
    private readonly string _original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;

    public void Dispose() => LanguageManager.SetLanguage(_original);

    private static IEnumerable<Func<Control>> Pages() => new Func<Control>[]
    {
        () => new WelcomePage(),
        () => new LicensePage(),
        () => new FolderPage(),
        () => new StartMenuPage(),
        () => new AdditionalTasksPage(),
        () => new ReadyPage(),
        () => new CompletePage(),
        () => new ErrorPage(),
    };

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    private static (Form Host, Control Page) Prepare(Func<Control> factory)
    {
        var host = new Form { ClientSize = new Size(640, 420) };
        var page = factory();
        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        host.CreateControl();

        var project = new Beep.Installer.Models.InstallProject
        {
            AppName = "برنامج إدارة البيانات",
            AppVersion = "1.0.0",
            AppPublisher = "شركة كونتوسو",
            DefaultDirName = @"C:\Program Files\Contoso",
            DefaultGroupName = "Contoso"
        };
        if (page is IInstallerPage installerPage)
        {
            try { installerPage.OnEnter(new InstallContext { Project = project, InstallPath = project.DefaultDirName }); }
            catch { /* entering is not what this suite tests */ }
        }

        host.PerformLayout();
        return (host, page);
    }

    // ── 7.B.2 · right-to-left ───────────────────────────────────────────────

    [Fact]
    public void ArabicMirrorsEveryControl_NotJustTheForm()
    {
        // Setting RightToLeft on a form does not reach children that have their own value. A page
        // that stays left-to-right inside a mirrored window is the single most visible RTL defect.
        using var form = new Form();
        var panel = new Panel();
        var label = new Label { Text = "مرحبا" };
        var box = new TextBox();
        panel.Controls.AddRange(new Control[] { label, box });
        form.Controls.Add(panel);

        Rtl.ApplyDirection(form, rightToLeft: true);

        form.RightToLeft.Should().Be(RightToLeft.Yes);
        form.RightToLeftLayout.Should().BeTrue("the window itself must mirror, not only its text");
        foreach (var control in Descendants(form))
            control.RightToLeft.Should().Be(RightToLeft.Yes, $"{control.GetType().Name} stayed left-to-right");
    }

    [Fact]
    public void SwitchingBackToEnglishUnmirrorsCompletely()
    {
        // The regression that shipped once: ApplyRtl was one-way, so Arabic → English left the
        // window mirrored with no way back short of restarting.
        using var form = new Form();
        var box = new TextBox();
        form.Controls.Add(box);

        Rtl.ApplyDirection(form, rightToLeft: true);
        Rtl.ApplyDirection(form, rightToLeft: false);

        form.RightToLeft.Should().Be(RightToLeft.No);
        form.RightToLeftLayout.Should().BeFalse();
        box.RightToLeft.Should().Be(RightToLeft.No);
    }

    [Fact]
    public void ArabicStringsAreActuallyArabic_NotEnglishFallback()
    {
        // Parity of *keys* is already guarded. This checks the values: a culture whose entries are
        // all English placeholders passes a key-parity test while being untranslated.
        LanguageManager.SetLanguage("ar");

        var samples = new[] { "Btn_Cancel", "Btn_Install", "Wizard_Welcome", "Common_Close" }
            .Select(LanguageManager.GetString)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        samples.Should().NotBeEmpty();
        // Inlined rather than a local function: an expression tree cannot reference one.
        samples.Should().OnlyContain(v => v.Any(c => c >= '\u0600' && c <= '\u06FF'),
            "these keys are translated, so Arabic must not be reading English: " + string.Join(" | ", samples));
    }

    [Fact]
    public void WizardPagesDoNotClipWhenMirrored()
    {
        // Mirroring changes where anchored content lands; text that fitted left-to-right can run off
        // the other edge.
        var offenders = new List<string>();

        foreach (var factory in Pages())
        {
            var (host, page) = Prepare(factory);
            using var _ = host;

            Rtl.ApplyDirection(host, rightToLeft: true);
            host.PerformLayout();

            foreach (Control child in page.Controls)
            {
                if (child.Dock != DockStyle.None || !child.Visible) continue;
                if (child.Left < 0 || child.Right > page.ClientSize.Width)
                    offenders.Add($"{page.GetType().Name}/{child.GetType().Name} spans {child.Left}..{child.Right} " +
                                  $"outside 0..{page.ClientSize.Width} when mirrored");
            }
        }

        offenders.Should().BeEmpty("mirrored pages must stay inside their bounds:" +
                                   Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheWizardPutsALanguageSwitcherOnScreen()
    {
        // 7.B.1. The plumbing for live switching -- LanguageChanged, ReloadStrings, two-way
        // ApplyDirection -- has existed for a while, but until now nothing placed a control, so an
        // end user could not reach any of it. Source-level because constructing the runtime wizard
        // needs a message loop.
        var source = ReadRepoFile(System.IO.Path.Combine("Beep.Installer", "Forms", "BeepModernInstallerForm.cs"));

        source.Should().Contain("_languageBox", "the wizard must expose a language picker");
        source.Should().Contain("LanguageManager.SetLanguage(choice.Code)",
            "choosing a language must actually switch it, not just change the caption");
    }

    [Fact]
    public void EverySupportedCultureHasANativeName()
    {
        // The picker shows each language in its own script; a culture falling through to "English"
        // would be indistinguishable from English in the list.
        var names = LanguageManager.SupportedCultures
            .ToDictionary(c => c, LanguageManager.NativeNameOf);

        names.Should().HaveCount(LanguageManager.SupportedCultures.Length);
        names.Values.Should().OnlyHaveUniqueItems(
            "two cultures showing the same name cannot be told apart in the picker: " +
            string.Join(", ", names.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    // ── 7.M.1 · the automatable part of an accessibility pass ────────────────

    [Fact]
    public void EveryInteractiveControlOnAWizardPageHasAnAccessibleName()
    {
        // What a screen reader announces. A control with no name is read as its bare type -- "edit",
        // "button" -- which tells the user nothing about what it does.
        var offenders = new List<string>();

        foreach (var factory in Pages())
        {
            var (host, page) = Prepare(factory);
            using var _ = host;

            A11y.EnsureAccessibility(host);

            foreach (var control in Descendants(page))
            {
                if (!A11y.IsInteractive(control) || !control.Visible) continue;
                if (!string.IsNullOrWhiteSpace(control.AccessibleName)) continue;
                if (!string.IsNullOrWhiteSpace(control.Text)) continue;

                offenders.Add($"{page.GetType().Name}/{control.GetType().Name} (name: '{control.Name}')");
            }
        }

        offenders.Should().BeEmpty("a screen reader cannot describe these:" +
                                   Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryInteractiveControlIsReachableByKeyboard()
    {
        // Accessibility Insights flags controls that the Tab key can never reach. A user who cannot
        // use a mouse simply cannot operate them.
        var offenders = new List<string>();

        foreach (var factory in Pages())
        {
            var (host, page) = Prepare(factory);
            using var _ = host;

            A11y.EnsureAccessibility(host);

            foreach (var control in Descendants(page))
            {
                if (!A11y.IsInteractive(control) || !control.Visible || !control.Enabled) continue;
                if (control is Label or LinkLabel) continue;   // not tab stops by convention
                if (control.TabStop) continue;

                offenders.Add($"{page.GetType().Name}/{control.GetType().Name} '{control.Name}' is not a tab stop");
            }
        }

        offenders.Should().BeEmpty("keyboard users cannot reach these:" +
                                   Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ForegroundTextMeetsWcagAaContrast()
    {
        // 4.5:1 is the WCAG AA threshold for body text and is exactly what an automated
        // accessibility scan measures. Controls left at system defaults are skipped: those are the
        // OS's contract with the user, not ours.
        var offenders = new List<string>();

        foreach (var factory in Pages())
        {
            var (host, page) = Prepare(factory);
            using var _ = host;

            foreach (var control in Descendants(page))
            {
                if (string.IsNullOrWhiteSpace(control.Text)) continue;

                var fore = control.ForeColor;
                var back = EffectiveBackColor(control);
                if (fore.A == 0 || back.A == 0) continue;

                var ratio = ContrastRatio(fore, back);
                if (ratio < 4.5)
                {
                    offenders.Add($"{page.GetType().Name}/{control.GetType().Name} " +
                                  $"{fore.Name} on {back.Name} = {ratio:F2}:1");
                }
            }
        }

        offenders.Should().BeEmpty("body text below 4.5:1 fails WCAG AA:" +
                                   Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, relativePath));
    }

    private static Color EffectiveBackColor(Control control)
    {
        // Walk up until something declares an opaque background; Transparent means "whatever is
        // behind me", and comparing against it would measure nothing.
        for (var c = control; c != null; c = c.Parent)
            if (c.BackColor.A != 0 && c.BackColor != Color.Transparent)
                return c.BackColor;

        return SystemColors.Control;
    }

    /// <summary>WCAG 2.1 relative-luminance contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (lighter, darker) = la >= lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
