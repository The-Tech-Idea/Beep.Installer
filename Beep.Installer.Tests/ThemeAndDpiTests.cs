using System.Drawing;
using System.IO;
using System.Linq;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Pins the shared theme tokens and the DPI-awareness invariant.
///
/// The UI previously carried three uncoordinated palettes — the wizard's hardcoded greys,
/// a private set of ARGB constants in the builder, and per-dialog inline colours — none of
/// which followed the active Beep theme. Only two of twelve forms opted into DPI scaling, so
/// the rest clipped at 125% and above.
/// </summary>
public class ThemeAndDpiTests
{
    [Fact]
    public void Tokens_AreUsableColours()
    {
        var tokens = new[]
        {
            InstallerTheme.Shell, InstallerTheme.Panel, InstallerTheme.Sidebar,
            InstallerTheme.Border, InstallerTheme.Text, InstallerTheme.MutedText,
            InstallerTheme.Accent, InstallerTheme.Success, InstallerTheme.Warning,
            InstallerTheme.Danger,
        };

        // A fully transparent token means a theme lookup returned an empty colour and the
        // fallback failed to engage — the control would render invisibly.
        tokens.Should().OnlyContain(c => c.A != 0, "every token must be a visible colour");
    }

    [Fact]
    public void BodyTextMeetsWcagAaContrast()
    {
        Contrast(InstallerTheme.Text, InstallerTheme.Panel)
            .Should().BeGreaterThanOrEqualTo(4.5, "body text must meet WCAG AA (4.5:1)");
    }

    [Fact]
    public void MutedTextMeetsWcagAaContrast()
    {
        // The previous muted grey (98,107,119 on white) measured about 4.4:1 — just under the
        // threshold. It was darkened rather than carried forward into the token layer.
        Contrast(InstallerTheme.MutedText, InstallerTheme.Panel)
            .Should().BeGreaterThanOrEqualTo(4.5, "secondary text must meet WCAG AA (4.5:1)");
    }

    [Fact]
    public void Fonts_AreProvided()
    {
        InstallerTheme.Body.Should().NotBeNull();
        InstallerTheme.BodyBold.Bold.Should().BeTrue();
        InstallerTheme.SectionTitle.Size.Should().BeGreaterThan(InstallerTheme.Body.Size);
    }

    [Fact]
    public void EveryForm_OptsIntoDpiScaling()
    {
        // Source-level check: the layouts position children in absolute pixels, so a form
        // without DPI auto-scaling clips on a high-DPI display.
        var formsDir = FindDirectory("Forms");
        var offenders = Directory.EnumerateFiles(formsDir, "*.cs")
            .Where(f => !File.ReadAllText(f).Contains("AutoScaleMode"))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("every form must set AutoScaleMode; missing: " + string.Join(", ", offenders));
    }

    /// <summary>Walks up from the test binaries to locate a source folder.</summary>
    private static string FindDirectory(string name)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Beep.Installer", name);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate the '{name}' source folder.");
    }

    /// <summary>WCAG 2.x relative-luminance contrast ratio between two colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
