using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Every screen in the builder opens, and shows something.
///
/// The nav-parity guard proves each section has a way in and a dispatch case. It does not open any
/// of them, so a section could be reachable and still throw, or render an empty panel, the moment a
/// user clicked it — which is what "the screens have errors" looks like from the outside.
///
/// This walks all forty, on a real form, against two projects: an empty one (every collection at
/// zero, which is what a first-time author sees) and a populated one (every collection non-empty,
/// which is what exercises the grids and their bindings).
/// </summary>
public sealed class EverySectionRendersTests
{
    /// <summary>A project with nothing authored — the state a new user starts in.</summary>
    private static InstallProject Empty() => InstallerProjectFactory.CreateNew("Blank", "1.0.0", "ACME", "");

    /// <summary>A project with at least one of everything the builder can author.</summary>
    private static InstallProject Populated()
    {
        var project = InstallerProjectFactory.CreateNew("Contoso", "2.1.0", "Contoso Ltd", "");

        project.Components.Add(new InstallComponent { Id = "core", Name = "Core", Selected = true });
        project.Prerequisites.Add(new Prerequisite { Id = "dotnet", Name = ".NET Desktop Runtime" });
        project.Resources.Add(new Beep.Installer.Engine.CompiledInstallOperation
        {
            Id = "file.copy:1",
            Type = "file.copy",
            DisplayName = "Copy the app",
        });

        return project;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    [Fact]
    public void EverySectionOpensOnAnEmptyProject() => AssertEverySectionRenders(Empty(), "an empty project");

    [Fact]
    public void EverySectionOpensOnAPopulatedProject() => AssertEverySectionRenders(Populated(), "a populated project");

    private static void AssertEverySectionRenders(InstallProject project, string what)
    {
        var failures = new List<string>();
        var opened = 0;

        RunSta(() =>
        {
            using var form = new PackageBuilderForm(new InstallerController(project));
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();

            var sections = form.SectionIds.ToList();
            sections.Should().NotBeEmpty("the builder must offer some sections");

            foreach (var id in sections)
            {
                try
                {
                    form.OpenSection(id);
                    Application.DoEvents();

                    var active = form.ActiveSection;
                    if (active == null)
                    {
                        failures.Add($"{id}: opened nothing");
                        continue;
                    }

                    if (!Descendants(active).Any())
                        failures.Add($"{id}: rendered an empty panel");

                    opened++;
                }
                catch (Exception ex)
                {
                    // The whole point: report every broken screen, not just the first.
                    failures.Add($"{id}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        opened.Should().BeGreaterThan(0, "the walk must actually open sections");
        failures.Should().BeEmpty($"every section must open on {what}:{Environment.NewLine}"
                                  + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EverySectionSurvivesItsOwnAddAndRemoveButtons()
    {
        // Opening a screen is not the same as using it. The grid sections all carry Add / Duplicate
        // / Remove, and those are where the binding paths actually run -- the reflection-based clone,
        // the ObservableCollection round trip, the property grid rebind. A screen can open cleanly
        // and still throw the moment the author presses the only button on it.
        //
        // Buttons that open a dialog are skipped on purpose: a modal in a test run blocks forever.
        var failures = new List<string>();
        var pressed = 0;

        RunSta(() =>
        {
            var project = Populated();
            using var form = new PackageBuilderForm(new InstallerController(project));
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();

            foreach (var id in form.SectionIds.ToList())
            {
                form.OpenSection(id);
                Application.DoEvents();

                if (form.ActiveSection is not { } active) continue;

                foreach (var button in Descendants(active).OfType<Button>().Where(IsSafeToPress).ToList())
                {
                    try
                    {
                        button.PerformClick();
                        Application.DoEvents();
                        pressed++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{id} / '{button.Text}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        });

        pressed.Should().BeGreaterThan(0, "the walk must actually press buttons");
        failures.Should().BeEmpty($"pressing a section's own buttons must not fail:{Environment.NewLine}"
                                  + string.Join(Environment.NewLine, failures));
    }

    /// <summary>Buttons that act on the list in place, rather than opening something modal.</summary>
    private static bool IsSafeToPress(Button button)
    {
        var text = button.Text ?? "";

        // An ellipsis is this codebase's convention for "opens a dialog".
        if (text.Contains("...", StringComparison.Ordinal) || text.Contains('…')) return false;

        return text.Contains("Add", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Remove", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Duplicate", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>WinForms needs a single-threaded apartment; xUnit threads are MTA.</summary>
    private static void RunSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            // Fail, do not hang.
            //
            // WinForms' default handler turns an exception thrown while a form is coming up into a
            // modal ThreadExceptionDialog, and a modal in a test run blocks forever. That is exactly
            // how ComponentConditionsDialog threw on every open without anything noticing: the run
            // stopped rather than reported. Rethrowing makes a broken form a failing test.
            //
            // This call itself can throw (SetUnhandledExceptionMode requires no window handle has
            // been created yet on this thread, and the full suite runs many WinForms tests before
            // this one). Left unguarded, that throw happens on a background thread with nothing
            // above it to catch it, which crashes the whole test host process rather than failing
            // one test -- exactly what took the full suite down.
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); }
            catch { /* best-effort; a hang is still possible but the process survives */ }

            try { action(); }
            catch (Exception ex) { failure = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
