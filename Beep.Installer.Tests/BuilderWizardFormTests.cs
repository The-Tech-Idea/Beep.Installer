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
[Collection("Language")]
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

            var componentsBtn = Descendants(wizard).OfType<Button>().First(b => b.Text == "Files and components");
            componentsBtn.FlatStyle.Should().Be(FlatStyle.Popup, "the step matching Advanced mode's current section should be the one shown, not Identity");
        });
    }

    [Fact]
    public void NextRefusesToLeaveComponentsEmpty()
    {
        // Field-level validation (ActiveSectionHasErrors) cannot see "the Components grid has zero
        // rows" -- that grid isn't a named TextBox/ComboBox/NumericUpDown. 12.D.4 adds this one rule
        // directly since it is the step most commonly needing at least one row.
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();

            Descendants(wizard).OfType<TextBox>().First().Text = "Contoso App";
            FindButton(wizard, "Next >").PerformClick(); // step 0 -> identity
            FindButton(wizard, "Next >").PerformClick(); // identity -> source
            FindButton(wizard, "Next >").PerformClick(); // source -> components

            FindButton(wizard, "Next >").PerformClick(); // components -> should stay put: empty

            FindButton(wizard, "Files and components").FlatStyle.Should().Be(FlatStyle.Popup, "Next must refuse to leave Components empty");

            controller.Project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent { Id = "core", Name = "Core" });
            FindButton(wizard, "Next >").PerformClick(); // components -> shortcuts, now allowed

            FindButton(wizard, "Install behavior").FlatStyle.Should().Be(FlatStyle.Popup);
        });
    }

    /// <summary>WinForms needs a single-threaded apartment; xUnit threads are MTA.</summary>
    [Fact]
    public void StepJumpCannotBypassProjectCreation()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            Descendants(wizard).OfType<TextBox>().First().Text = "";
            FindButton(wizard, "Review and build").PerformClick();
            wizard.CurrentSectionId.Should().BeNull();
            FindButton(wizard, "< Back").Enabled.Should().BeFalse();

            Descendants(wizard).OfType<TextBox>().First().Text = "Created by jump";
            FindButton(wizard, "Review and build").PerformClick();
            controller.Project.AppName.Should().Be("Created by jump");
            wizard.CurrentStepId.Should().Be("files", "empty required components cannot be jumped over");
        });
    }

    [Fact]
    public void StepJumpValidatesAnIntermediateCollection()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            controller.Project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent { Id = "", Name = "Invalid component" });
            FindButton(wizard, "Review and build").PerformClick();
            wizard.CurrentStepId.Should().Be("files");
            Descendants(wizard).OfType<Label>().Should().Contain(l => l.Text.Contains("Component Id is required"));
        });
    }

    [Fact]
    public void WarningsRemainVisibleButAllowNavigation()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            FindButton(wizard, "< Back").PerformClick();
            Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppPublisher").Text = "";
            FindButton(wizard, "Next >").PerformClick();
            ProjectSchemaService.Validate(controller.Project).Diagnostics.Should().Contain(d => d.Code == "BI1103" && d.Severity == ProjectSchemaDiagnosticSeverity.Warning);
            wizard.CurrentSectionId.Should().Be("source");
        });
    }

    [Fact]
    public void ImmediateEditAndNextUsesCurrentValuesRatherThanDebounceState()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            FindButton(wizard, "< Back").PerformClick();
            var name = Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppName");
            name.Text = "";
            FindButton(wizard, "Next >").PerformClick();
            wizard.CurrentStepId.Should().Be("project");
            name.Text = "Corrected immediately";
            FindButton(wizard, "Next >").PerformClick();
            wizard.CurrentSectionId.Should().Be("source");
            controller.Project.AppName.Should().Be("Corrected immediately");
        });
    }

    [Fact]
    public void RevisitingProjectAppliesEditsWithoutReplacingIdentityOrResources()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            var original = controller.Project;
            var appId = original.AppId;
            original.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent { Id = "core", Name = "Core" });
            FindButton(wizard, "< Back").PerformClick();
            Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppName").Text = "Renamed product";
            Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppVersion").Text = "3.0.0";
            FindButton(wizard, "Next >").PerformClick();
            controller.Project.Should().BeSameAs(original);
            original.AppId.Should().Be(appId);
            original.AppName.Should().Be("Renamed product");
            original.AppVersion.Should().Be("3.0.0");
            original.Components.Should().ContainSingle();
        });
    }

    [Fact]
    public void NewDraftDoesNotDiscardTheCurrentProjectBeforeCreation()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            controller.New("Existing", "4.0.0", "Publisher", "source");
            var original = controller.Project;
            using var advanced = new PackageBuilderForm(controller);
            using var wizard = new BuilderWizardForm(controller, advanced, startNew: true);
            wizard.Show();
            wizard.CurrentSectionId.Should().BeNull();
            controller.Project.Should().BeSameAs(original);
            wizard.Close();
            controller.Project.Should().BeSameAs(original);
        });
    }

    [Fact]
    public void DisposingGuidedReturnsItsSharedPanelToTheExistingEditor()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            controller.New("Existing", "1.0.0", "Publisher", "source");
            using var advanced = new PackageBuilderForm(controller);
            advanced.OpenSection("source");
            var panel = advanced.ActiveSection;
            using (var wizard = new BuilderWizardForm(controller, advanced))
            {
                wizard.Show();
                panel!.Parent.Should().NotBeNull();
            }
            advanced.IsDisposed.Should().BeFalse();
            panel!.IsDisposed.Should().BeFalse();
            advanced.OpenSection("source");
            advanced.ActiveSection.Should().BeSameAs(panel);
            controller.Project.AppName = "Still editable";
            advanced.ActiveSection!.Controls.Count.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void FilesOnlyChoiceReachesReviewAndReviewLinksReturnToTheCorrectTab()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            Descendants(wizard).OfType<CheckBox>().Single(c => c.Name == "FilesOnly").Checked = true;
            FindButton(wizard, "Review and build").PerformClick();
            wizard.CurrentStepId.Should().Be("review");
            UiStaTest.Pump(Descendants(wizard).OfType<GuidedReviewPage>().Single().PendingCheck!);
            Descendants(wizard).OfType<Button>().Single(b => b.Name == "GuidedBuild").Enabled.Should().BeTrue();
            Descendants(wizard).OfType<Button>().Single(b => Equals(b.Tag, "Setup.DefaultScope")).PerformClick();
            wizard.CurrentStepId.Should().Be("install");
            wizard.CurrentSectionId.Should().Be("layout");
            Descendants(wizard).OfType<TabControl>().Single(t => t.Name == "GuidedSections").SelectedTab!.Name.Should().Be("layout");
        });
    }

    [Fact]
    public void ClickingTheCurrentProjectStepAndOpeningAdvancedPreservesDraftEdits()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            var name = Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppName");
            name.Text = "Draft name";
            FindButton(wizard, "Project").PerformClick();
            Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppName").Text.Should().Be("Draft name");
            FindButton(wizard, "Next >").PerformClick();
            FindButton(wizard, "< Back").PerformClick();
            Descendants(wizard).OfType<TextBox>().Single(t => t.Name == "AppName").Text = "Edited before handoff";
            FindButton(wizard, "Open full editor").PerformClick();
            controller.Project.AppName.Should().Be("Edited before handoff");
            wizard.AdvancedForm.ActiveSectionId.Should().Be("identity");
        });
    }

    [Fact]
    public void ReopeningResultsLaunchesOnlyTheLatestArtifactOnce()
    {
        RunSta(() =>
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BeepResultActions_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            try
            {
                var first = System.IO.Path.Combine(root, "first.application");
                var latest = System.IO.Path.Combine(root, "latest.application");
                System.IO.File.WriteAllText(first, "manifest");
                System.IO.File.WriteAllText(latest, "manifest");
                using var editor = new PackageBuilderForm(new InstallerController());
                editor.Show();
                var launched = new System.Collections.Generic.List<string>();
                editor.ArtifactLauncher = start => launched.Add(start.FileName);
                editor.RecordLastBuild(new BuildPipeline.BuildResult { Success = true, ArtifactKind = "clickonce", OutputFile = first });
                editor.OpenSection("result");
                editor.OpenSection("identity");
                editor.RecordLastBuild(new BuildPipeline.BuildResult { Success = true, ArtifactKind = "clickonce", OutputFile = latest });
                editor.OpenSection("result");
                editor.OpenSection("result");
                Descendants(editor).OfType<Button>().Single(b => b.Name == "RunInstallerBtn").PerformClick();
                launched.Should().Equal(latest);
            }
            finally { System.IO.Directory.Delete(root, recursive: true); }
        });
    }

    [Fact]
    public void PackageStepBlocksUnsupportedMsixConfigurationBeforeReview()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            FindButton(wizard, "Next >").PerformClick();
            Descendants(wizard).OfType<CheckBox>().Single(c => c.Name == "FilesOnly").Checked = true;
            controller.Project.OutputFormat = Beep.Installer.Models.InstallerOutputFormat.Msix;
            FindButton(wizard, "Review and build").PerformClick();
            wizard.CurrentStepId.Should().Be("package");
            Descendants(wizard).OfType<Label>().Should().Contain(l => l.Text.Contains("identity", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Theory]
    [InlineData("WindowsServices[2].Name", "windowsservices")]
    [InlineData("RegistryEntries[1].KeyPath", "registry")]
    [InlineData("Setup.OutputFormat", "package")]
    [InlineData("Setup.SourceDirectory", "source")]
    public void ReviewDiagnosticPathsIdentifyTheCorrectEditor(string path, string section)
        => Beep.Installer.Ui.AuthoringDiagnosticNavigation.SectionForPath(path).Should().Be(section);

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
