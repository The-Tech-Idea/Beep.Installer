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
using A11y = Beep.Installer.Engine.Accessibility;

namespace Beep.Installer.Tests;

/// <summary>
/// Every window the product can open, opened.
///
/// The builder's forty sections were covered by <see cref="EverySectionRendersTests"/>, and the five
/// wizards have their own classes. That still left a dozen forms — the runtime installer wizard
/// itself, the build result and error windows, the component and custom-action dialogs, the template
/// and project dialogs, the wizard preview — with nothing that had ever constructed them. A form
/// that throws in its constructor is invisible until someone opens the menu item.
///
/// Each is built with plausible arguments, shown, and checked for three things: it renders
/// something, its own in-place buttons do not throw, and its interactive controls are named for a
/// screen reader.
/// </summary>
public sealed class EveryDialogRendersTests
{
    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("Contoso", "2.1.0", "Contoso Ltd", "");
        project.Components.Add(new InstallComponent { Id = "core", Name = "Core", Selected = true });
        return project;
    }

    /// <summary>Every dialog, as a name and a way to build it.</summary>
    public static TheoryData<string> DialogNames() => new()
    {
        "BeepModernInstallerForm",
        "BuildErrorForm",
        "BuildProgressForm",
        "BuildResultForm",
        "ComponentConditionsDialog",
        "ComponentFilesDialog",
        "CustomActionsDialog",
        "LanguageManagerForm",
        "ProjectNewDialog",
        "TemplateUpdateDialog",
        "WizardPreviewForm",
    };

    private static Form Create(string name)
    {
        var project = Project();

        return name switch
        {
            // Preview mode: the runtime wizard must not start installing anything.
            "BeepModernInstallerForm" => new BeepModernInstallerForm(project, previewMode: true),

            "BuildErrorForm" => new BuildErrorForm(
                "Build failed", "One step did not complete.", "Detail goes here.",
                new[] { "log line one", "log line two" }),

            "BuildProgressForm" => new BuildProgressForm("Building", "Packaging payload..."),

            "BuildResultForm" => new BuildResultForm(
                new Beep.Installer.Engine.BuildPipeline.BuildResult { Success = true, OutputFile = @"C:\out\Setup.exe" },
                project.AppName),

            "ComponentConditionsDialog" => new ComponentConditionsDialog(project.Components, project.Components[0]),

            "ComponentFilesDialog" => new ComponentFilesDialog(project.Components[0], null),

            "CustomActionsDialog" => new CustomActionsDialog(project.CustomActions),

            "LanguageManagerForm" => new LanguageManagerForm(),

            "ProjectNewDialog" => new ProjectNewDialog(),

            "TemplateUpdateDialog" => new TemplateUpdateDialog(
                Array.Empty<ProjectTemplate>(),
                _ => new ProjectTemplateUpdatePreview()),

            "WizardPreviewForm" => new WizardPreviewForm(project),

            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no factory for this dialog"),
        };
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void ItOpensAndShowsSomething(string name)
    {
        RunSta(() =>
        {
            using var form = Create(name);
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();

            Descendants(form).Should().NotBeEmpty($"{name} must render something");
        });
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void ItsOwnInPlaceButtonsDoNotThrow(string name)
    {
        // Add / Remove / Duplicate act on the list in front of them. Anything with an ellipsis opens
        // something modal and would block the run, so it is left alone.
        var failures = new List<string>();

        RunSta(() =>
        {
            using var form = Create(name);
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();

            foreach (var button in Descendants(form).OfType<Button>().Where(IsSafeToPress).ToList())
            {
                try
                {
                    button.PerformClick();
                    Application.DoEvents();
                }
                catch (Exception ex)
                {
                    failures.Add($"'{button.Text}': {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        failures.Should().BeEmpty($"{name} must survive its own buttons:{Environment.NewLine}"
                                  + string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void ItsControlsAreNamedForAScreenReader(string name)
    {
        var unnamed = new List<string>();

        RunSta(() =>
        {
            using var form = Create(name);
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            A11y.EnsureAccessibility(form);

            unnamed.AddRange(Descendants(form)
                .Where(c => A11y.IsInteractive(c) && c.Visible)
                .Where(c => string.IsNullOrWhiteSpace(c.AccessibleName) && string.IsNullOrWhiteSpace(c.Text))
                .Select(c => c.GetType().Name));
        });

        unnamed.Should().BeEmpty($"{name} has unnamed controls: " + string.Join(", ", unnamed));
    }

    private static bool IsSafeToPress(Button button)
    {
        var text = button.Text ?? "";

        if (text.Contains("...", StringComparison.Ordinal) || text.Contains('\u2026')) return false;

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
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

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
