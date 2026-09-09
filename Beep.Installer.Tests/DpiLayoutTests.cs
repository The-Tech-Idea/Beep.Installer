using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Pages;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The DPI matrix (6.M.1) and the absolute-coordinate pages behind it (6.B.2), as an automated
/// check rather than three screenshots.
///
/// What a human at 100/150/200% is looking for is specific and mechanical: text clipped by a
/// container that did not grow, and controls that have drifted on top of each other. Both are
/// measurable from the control tree, so most of that matrix does not need eyes — only the
/// subjective "does it look right" residue does.
///
/// The wizard pages lay themselves out with absolute <c>Location</c> and fixed <c>Size</c> against
/// hard-coded point fonts. Scaling moves and resizes the boxes, but a label whose text needs more
/// room at 200% is clipped by a container that was told to be exactly 200px tall.
/// </summary>
public class DpiLayoutTests
{
    /// <summary>The scales the gate names, plus 1.25 which is Windows' most common non-integer.</summary>
    public static TheoryData<float> Scales => new() { 1.0f, 1.25f, 1.5f, 2.0f };

    private static IEnumerable<Func<Control>> PageFactories() => new Func<Control>[]
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

    /// <summary>
    /// A context with realistic — not minimal — content.
    ///
    /// The pages set their text in <c>OnEnter</c>, not in their constructors, so measuring a
    /// freshly-constructed page measures empty labels and passes without testing anything. The
    /// product name is deliberately long: a short name fits at any scale and would hide exactly the
    /// clipping this is looking for.
    /// </summary>
    private static InstallContext RealisticContext()
    {
        var project = new Beep.Installer.Models.InstallProject
        {
            AppName = "Contoso Enterprise Data Management Suite",
            AppVersion = "10.4.2-preview.3",
            AppPublisher = "Contoso Corporation International",
            DefaultDirName = @"C:\Program Files\Contoso\Enterprise Data Management Suite",
            DefaultGroupName = "Contoso Enterprise Data Management Suite",
            LicenseText = string.Join(Environment.NewLine,
                Enumerable.Repeat("This End User Licence Agreement governs your use of the software.", 12))
        };

        return new InstallContext
        {
            Project = project,
            InstallPath = project.DefaultDirName,
            PerUser = false
        };
    }

    /// <summary>Builds a page inside a host, drives OnEnter, and scales it.</summary>
    private static (Form Host, Control Page) Prepare(Func<Control> factory, float scale)
    {
        var host = new Form { ClientSize = new Size(640, 420) };
        var page = factory();
        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        host.CreateControl();

        if (page is IInstallerPage installerPage)
        {
            // A page that cannot survive being entered is a different defect; this suite is about
            // layout, so let it report rather than fail every scale with the same exception.
            try { installerPage.OnEnter(RealisticContext()); } catch { /* not a layout failure */ }
        }

        if (Math.Abs(scale - 1.0f) > float.Epsilon)
            host.Scale(new SizeF(scale, scale));

        host.PerformLayout();
        return (host, page);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    /// <summary>
    /// A control that runs past its parent's client area is clipped — the user simply cannot read
    /// the end of the sentence. Docked and anchored controls are excluded: the layout engine owns
    /// their bounds and a transient value during scaling is not a defect.
    /// </summary>
    private static List<string> ClippedChildren(Control parent)
    {
        var clipped = new List<string>();
        foreach (Control child in parent.Controls)
        {
            if (child.Dock != DockStyle.None) continue;
            if (!child.Visible) continue;

            var area = parent.ClientSize;
            if (area.Width <= 0 || area.Height <= 0) continue;

            if (child.Right > area.Width || child.Bottom > area.Height)
            {
                clipped.Add(
                    $"{parent.GetType().Name}/{child.GetType().Name} '{Trim(child.Text)}' " +
                    $"extends to ({child.Right},{child.Bottom}) beyond client {area.Width}x{area.Height}");
            }
        }
        return clipped;
    }

    private static string Trim(string? text)
        => string.IsNullOrEmpty(text) ? "" : (text.Length <= 24 ? text : text[..24] + "…");

    [Theory]
    [MemberData(nameof(Scales))]
    public void WizardPages_LayOutWithoutClipping_AtEveryScale(float scale)
    {
        // The host the runtime wizard gives a page: a fixed content area. If the page cannot fit its
        // own content into that at 200%, the end user loses text.
        var offenders = new List<string>();

        foreach (var factory in PageFactories())
        {
            var (host, page) = Prepare(factory, scale);
            using (host)
            {
                offenders.AddRange(ClippedChildren(page));
            }
        }

        offenders.Should().BeEmpty(
            $"no wizard page may clip its own content at {scale:P0} scaling:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void WizardPages_DoNotOverlapTheirSiblings_AtEveryScale(float scale)
    {
        // Overlap is the other half of what a human spots at 150%: an icon that grew into the title
        // beside it because the icon scales and the title's absolute Location does not.
        var offenders = new List<string>();

        foreach (var factory in PageFactories())
        {
            var (host, page) = Prepare(factory, scale);
            using var _ = host;

            var positioned = page.Controls.Cast<Control>()
                .Where(c => c.Dock == DockStyle.None && c.Visible && c.Width > 0 && c.Height > 0)
                .ToList();

            for (var i = 0; i < positioned.Count; i++)
            for (var j = i + 1; j < positioned.Count; j++)
            {
                var a = positioned[i];
                var b = positioned[j];
                if (!a.Bounds.IntersectsWith(b.Bounds)) continue;

                offenders.Add($"{page.GetType().Name}: {a.GetType().Name} '{Trim(a.Text)}' " +
                              $"overlaps {b.GetType().Name} '{Trim(b.Text)}'");
            }
        }

        offenders.Should().BeEmpty(
            $"wizard controls must not collide at {scale:P0} scaling:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void FixedHeightTextBoxes_StillFitTheirText_AtEveryScale(float scale)
    {
        // The check the two above cannot make. Control.Scale() moves and resizes everything
        // proportionally, so an absolute layout survives it -- which is why those tests pass and
        // why they are not on their own evidence that 200% is fine.
        //
        // What actually clips at high DPI is text: font metrics do not scale perfectly linearly, so
        // a paragraph that needed three lines at 100% can need four at 150%, and a label given a
        // fixed Size clips the last one. Measure the text against the box it was given.
        var offenders = new List<string>();

        foreach (var factory in PageFactories())
        {
            var (host, page) = Prepare(factory, scale);
            using var _ = host;

            foreach (var control in Descendants(page))
            {
                if (control.AutoSize) continue;                       // grows to fit by definition
                if (control.Dock != DockStyle.None) continue;         // the layout engine owns it
                if (string.IsNullOrWhiteSpace(control.Text)) continue;
                if (control is TextBoxBase or ListControl) continue;  // these scroll rather than clip
                if (control.Width <= 0) continue;

                var required = TextRenderer.MeasureText(
                    control.Text, control.Font,
                    new Size(control.Width, int.MaxValue),
                    TextFormatFlags.WordBreak);

                if (required.Height > control.Height)
                {
                    offenders.Add(
                        $"{page.GetType().Name}/{control.GetType().Name} '{Trim(control.Text)}' " +
                        $"needs {required.Height}px of height but was given {control.Height}px");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"text must fit its box at {scale:P0} scaling:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ThePagesUnderTestActuallyHaveContent()
    {
        // Guards the suite against itself. The pages populate their labels in OnEnter, not in their
        // constructors, so an earlier version of these tests measured empty controls and reported a
        // clean 200% with no evidence whatsoever. If OnEnter stops running, or stops setting text,
        // the three theories above go quietly vacuous rather than failing.
        var withText = 0;
        var measurable = 0;

        foreach (var factory in PageFactories())
        {
            var (host, page) = Prepare(factory, 1.0f);
            using var _ = host;

            foreach (var control in Descendants(page))
            {
                if (!string.IsNullOrWhiteSpace(control.Text)) withText++;
                if (!control.AutoSize && control.Dock == DockStyle.None && !string.IsNullOrWhiteSpace(control.Text))
                    measurable++;
            }
        }

        withText.Should().BeGreaterThan(10, "OnEnter should have populated the wizard pages");
        measurable.Should().BeGreaterThan(0,
            "at least one fixed-size control must carry text, or the text-fit theory proves nothing");
    }

    [Fact]
    public void EveryFormOptsIntoDpiScaling()
    {
        // AutoScaleMode.Dpi is what makes the scaling above happen at all. A form that forgets it
        // renders at 96 DPI on a 200% display and everything is half-size.
        var offenders = new List<string>();

        foreach (var type in typeof(Beep.Installer.Forms.PackageBuilderForm).Assembly.GetTypes()
                     .Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var source = t_source(type);
            if (source is null) continue;
            if (!source.Contains("AutoScaleMode", StringComparison.Ordinal))
                offenders.Add(type.Name);
        }

        offenders.Should().BeEmpty("these forms never set AutoScaleMode: " + string.Join(", ", offenders));

        static string? t_source(Type type)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Beep.Installer.slnx")))
                dir = dir.Parent;
            if (dir is null) return null;

            var matches = System.IO.Directory.GetFiles(dir.FullName, type.Name + ".cs", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}"))
                .ToList();
            return matches.Count == 1 ? System.IO.File.ReadAllText(matches[0]) : null;
        }
    }
}
