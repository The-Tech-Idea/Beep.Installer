using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// BuilderWizardForm is the default, primary window a cold start with nothing to resume now opens
/// (P12 §3.4, §5 12.D.2) instead of the flat PackageBuilderForm. These cover the stepper mechanics a
/// screenshot cannot: Next is gated on the new-project step, revisiting that step after creation must
/// not silently reset the project (a real bug caught in review before this shipped -- OnNext used to
/// re-run InstallerController.New unconditionally), and every golden-path step opens without throwing.
/// </summary>
public sealed class BuilderWizardFormTests
{
    private static Button FindButton(Control root, string text)
        => Descendants(root).OfType<Button>().First(b => b.Text == text);

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    [Fact]
    public void OpensOnTheNewProjectStepForABlankController()
    {
        RunSta(() =>
        {
            using var wizard = new BuilderWizardForm(new InstallerController());
            wizard.Show();

            FindButton(wizard, "< Back").Enabled.Should().BeFalse("step 0 has nothing before it");
        });
    }

    [Fact]
    public void NextRefusesAnEmptyProductName()
    {
        RunSta(() =>
        {
            using var wizard = new BuilderWizardForm(new InstallerController());
            wizard.Show();

            var nameBox = Descendants(wizard).OfType<TextBox>().First();
            nameBox.Text = "";

            FindButton(wizard, "Next >").PerformClick();

            // Still on step 0: Back stays disabled, and the wizard did not advance to Identity.
            FindButton(wizard, "< Back").Enabled.Should().BeFalse();
        });
    }

    [Fact]
    public void RevisitingTheNewProjectStepDoesNotResetAnAlreadyCreatedProject()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();

            var nameBox = Descendants(wizard).OfType<TextBox>().First();
            nameBox.Text = "Contoso App";
            FindButton(wizard, "Next >").PerformClick();

            controller.Project.AppName.Should().Be("Contoso App");

            // Edit further on the Identity step the wizard just opened, as a real author would.
            controller.Project.AppVersion = "2.5.0";

            // Back to step 0, then forward again -- this used to call InstallerController.New a
            // second time with the step's original placeholder text, wiping both edits above.
            FindButton(wizard, "< Back").PerformClick();
            FindButton(wizard, "Next >").PerformClick();

            controller.Project.AppName.Should().Be("Contoso App");
            controller.Project.AppVersion.Should().Be("2.5.0");
        });
    }

    [Fact]
    public void EveryGoldenPathStepOpensWithoutThrowing()
    {
        var failures = new System.Collections.Generic.List<string>();

        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();

            Descendants(wizard).OfType<TextBox>().First().Text = "Contoso App";
            FindButton(wizard, "Next >").PerformClick(); // step 0 -> identity
            Application.DoEvents();

            // Click Next repeatedly; a step whose own validation blocks forward movement just stays
            // put, which is fine -- the point is that opening each reachable step never throws.
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    var next = Descendants(wizard).OfType<Button>().FirstOrDefault(b => b.Text is "Next >" or "Finish");
                    if (next is null || !next.Visible) break;
                    next.PerformClick();
                    Application.DoEvents();
                }
                catch (Exception ex)
                {
                    failures.Add($"step {i}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void OpenFullEditorHandsTheCurrentProjectToAdvancedMode()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();

            Descendants(wizard).OfType<TextBox>().First().Text = "Contoso App";
            FindButton(wizard, "Next >").PerformClick();

            var handed = false;
            wizard.AdvancedRequested += (_, _) => handed = true;
            FindButton(wizard, "Open full editor").PerformClick();

            handed.Should().BeTrue();
            wizard.AdvancedForm.Project.Should().BeSameAs(controller.Project);
            wizard.AdvancedForm.ActiveSection.Should().NotBeNull("the current step's section must be handed back to Advanced mode, not left empty");
        });
    }

    [Fact]
    public void ReusesAnExistingAdvancedFormInsteadOfConstructingASecondOne()
    {
        // Advanced mode's own "Guided" button (P12 12.D.3) must not leave two PackageBuilderForm
        // instances both subscribed to the same InstallerController's events.
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var advanced = new PackageBuilderForm(controller);
            advanced.OpenSection("components");

            using var wizard = new BuilderWizardForm(controller, advanced);

            wizard.AdvancedForm.Should().BeSameAs(advanced);
        });
    }

    [Fact]
    public void StartsOnTheStepMatchingWhereAdvancedModeWas()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var advanced = new PackageBuilderForm(controller);
            advanced.OpenSection("components");

            using var wizard = new BuilderWizardForm(controller, advanced);
            wizard.Show();

            var componentsBtn = Descendants(wizard).OfType<Button>().First(b => b.Text == "Components");
            componentsBtn.FlatStyle.Should().Be(FlatStyle.Popup, "the step matching Advanced mode's current section should be the one shown, not Identity");
        });
    }

    /// <summary>WinForms needs a single-threaded apartment; xUnit threads are MTA.</summary>
    private static void RunSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
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
