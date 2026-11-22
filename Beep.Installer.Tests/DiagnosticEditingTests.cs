using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class DiagnosticEditingTests
{
    [Theory]
    [InlineData("SourceIncludes")]
    [InlineData("SourceExcludes")]
    public void IndexedPatternDiagnosticSelectsTheExactListAndAllowsRemovingItsRow(string property)
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.SourceIncludes.Clear(); project.SourceExcludes.Clear();
            project.SourceIncludes.Add("*.exe"); project.SourceIncludes.Add("*.dll");
            project.SourceExcludes.Add("*.pdb"); project.SourceExcludes.Add("*.tmp");
            using var editor = new PackageBuilderForm(new InstallerController(project)); editor.Show();
            editor.OpenDiagnostic("Setup." + property + "[1]"); editor.ActiveSectionId.Should().Be("includes");
            var list = Descendants(editor.ActiveSection!).OfType<ListBox>().Single(control => control.Name == property);
            var other = Descendants(editor.ActiveSection!).OfType<ListBox>().Single(control => control.Name != property);
            list.SelectedIndex.Should().Be(1); list.Focused.Should().BeTrue(); var otherSelection = other.SelectedIndex;
            var before = InstallerScriptSerializer.Write(project); var panel = editor.ActiveSection;
            editor.OpenDiagnostic("Setup." + property + "[99]"); list.SelectedIndex.Should().Be(1); other.SelectedIndex.Should().Be(otherSelection);
            InstallerScriptSerializer.Write(project).Should().Be(before);
            Descendants(editor.ActiveSection!).OfType<Button>().Single(control => control.Name == property + "Remove").PerformClick();
            var edited = property == "SourceIncludes" ? project.SourceIncludes : project.SourceExcludes;
            var untouched = property == "SourceIncludes" ? project.SourceExcludes : project.SourceIncludes;
            edited.Should().HaveCount(1); untouched.Should().HaveCount(2); editor.ActiveSection.Should().BeSameAs(panel);
            editor.OpenDiagnostic("Setup." + property); list.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("Setup")]
    [InlineData("Setup.")]
    [InlineData("Setup[0]")]
    public void RootOnlySetupDiagnosticsOpenIdentityWithoutThrowingOrChangingTheProject(string path)
    {
        RunSta(() =>
        {
            using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.OpenSection("build");
            var before = InstallerScriptSerializer.Write(editor.Project);
            editor.OpenDiagnostic(path); editor.ActiveSectionId.Should().Be("identity");
            InstallerScriptSerializer.Write(editor.Project).Should().Be(before);
        });
    }

    [Fact]
    public void NestedIisDiagnosticSelectsTheBindingAndItsPropertyWithoutReplacingTheSite()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            var first = new IisSiteDefinition { Name = "First" };
            var second = new IisSiteDefinition { Name = "Second" };
            second.Bindings.Add(new IisBindingDefinition { Port = 80 });
            second.Bindings.Add(new IisBindingDefinition { Port = 70000 });
            project.IisSites.Add(first);
            project.IisSites.Add(second);
            using var editor = new PackageBuilderForm(new InstallerController(project));
            editor.Show();
            editor.OpenDiagnostic("IisSites[1].Bindings[1].Port");
            editor.ActiveSectionId.Should().Be("iissites");
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedObject.Should().BeSameAs(second.Bindings[1]);
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("Port");
            properties.SelectedGridItem.PropertyDescriptor.SetValue(properties.SelectedObject, 8443);
            second.Bindings[1].Port.Should().Be(8443);
            project.IisSites[1].Should().BeSameAs(second);
            project.IisSites[0].Should().BeSameAs(first);

            // Moving to the owning row restores its editor after nested diagnostic navigation.
            editor.OpenDiagnostic("IisSites[0].Name");
            properties.SelectedObject.Should().BeSameAs(first);
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("Name");
        });
    }

    [Fact]
    public void ComponentFileDiagnosticEditsTheSelectedFileWithoutReplacingTheComponent()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            var component = new InstallComponent { Id = "core", Name = "Core" };
            component.Files.Add(new FileCopyOperation { SourcePath = "one.exe", DestinationPath = "one.exe" });
            component.Files.Add(new FileCopyOperation { SourcePath = "two.exe", DestinationPath = "" });
            project.Components.Add(component);
            using var editor = new PackageBuilderForm(new InstallerController(project));
            editor.Show();
            editor.OpenDiagnostic("Components[0].Files[1].DestinationPath");
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedObject.Should().BeSameAs(component.Files[1]);
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("DestinationPath");
            properties.SelectedGridItem.PropertyDescriptor.SetValue(properties.SelectedObject, "fixed.exe");
            component.Files[1].DestinationPath.Should().Be("fixed.exe");
            project.Components[0].Should().BeSameAs(component);
            component.Files[0].DestinationPath.Should().Be("one.exe");
        });
    }

    [Fact]
    public void InvalidNestedIndicesDoNotSelectAnUnrelatedObject()
    {
        var component = new InstallComponent { Id = "core" };
        component.Files.Add(new FileCopyOperation());
        DiagnosticEditorNavigator.TryResolve(component, new[] { "Files[9]", "DestinationPath" }, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SensitiveInputDiagnosticFocusesTheActualDictionaryEntryAndCanFixMissingValues(bool missing)
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            var first = new CompiledInstallOperation { Id = "first", Type = "contoso.provider" }; first.Inputs["opaque"] = "untouched";
            var resource = new CompiledInstallOperation { Id = "second", Type = "contoso.provider", SensitiveInputs = new() { "opaque" } };
            if (!missing) resource.Inputs["opaque"] = "invalid-credential";
            resource.Inputs["server"] = "server.example";
            project.Resources.Add(first); project.Resources.Add(resource);
            var diagnostic = ProjectSchemaService.Validate(project).Diagnostics.Single(item => item.Code == "BI1703");
            diagnostic.Path.Should().Be("Resources[1].Inputs.opaque");
            using var editor = new PackageBuilderForm(new InstallerController(project)); editor.Show(); editor.OpenDiagnostic(diagnostic.Path);
            editor.ActiveSectionId.Should().Be("resources");
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedObject.Should().BeOfType<DictionaryInputProperties>();
            var descriptor = properties.SelectedGridItem!.PropertyDescriptor!; descriptor.Name.Should().Be("opaque");
            ((PasswordPropertyTextAttribute)descriptor.Attributes[typeof(PasswordPropertyTextAttribute)]!).Password.Should().BeTrue();
            properties.ContainsFocus.Should().BeTrue();
            descriptor.SetValue(properties.SelectedObject, "secret://env/CONTOSO_INPUT");
            ProjectSchemaService.Validate(project).Diagnostics.Should().NotContain(item => item.Code == "BI1703");
            resource.Inputs["server"].Should().Be("server.example"); first.Inputs["opaque"].Should().Be("untouched");
            project.Resources[1].Should().BeSameAs(resource);
            editor.OpenDiagnostic("Resources[0].Id"); properties.SelectedObject.Should().BeSameAs(first);
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("Id");
        });
    }

    [Theory]
    [InlineData("binding.0.port")]
    [InlineData("argument-name")]
    [InlineData("names[0].key")]
    public void DictionaryKeyEditsPreserveSiblingsAndUseResourceUndoRedo(string key)
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            var resource = new CompiledInstallOperation { Id = "extension", Type = "contoso.provider" };
            resource.Inputs[key] = "70000"; resource.Inputs["binding.1.port"] = "443";
            project.Resources.Add(resource); var controller = new InstallerController(project);
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenDiagnostic("Resources[0].Inputs." + key);
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            var selected = properties.SelectedGridItem!; selected.PropertyDescriptor!.Name.Should().Be(key);
            selected.PropertyDescriptor.SetValue(properties.SelectedObject, "8443");
            typeof(PropertyGrid).GetMethod("OnPropertyValueChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(properties, new object[] { new PropertyValueChangedEventArgs(selected, "70000") });
            project.IsDirty.Should().BeTrue(); resource.Inputs["binding.1.port"].Should().Be("443");
            controller.CanUndo.Should().BeTrue(); controller.Undo();
            controller.Project.Resources.Single().Inputs[key].Should().Be("70000");
            controller.Redo(); controller.Project.Resources.Single().Inputs[key].Should().Be("8443");
            controller.Project.Resources.Single().Inputs["binding.1.port"].Should().Be("443");
        });
    }

    [Fact]
    public void InvalidNestedNavigationRestoresTheOwningResourceEditor()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            var site = new IisSiteDefinition { Name = "Site" }; site.Bindings.Add(new IisBindingDefinition { Port = 80 }); project.IisSites.Add(site);
            using var editor = new PackageBuilderForm(new InstallerController(project)); editor.Show(); editor.OpenDiagnostic("IisSites[0].Bindings[0].Port");
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single(); properties.SelectedObject.Should().BeSameAs(site.Bindings[0]);
            editor.OpenDiagnostic("IisSites[0].Bindings[99].Port"); properties.SelectedObject.Should().BeSameAs(site);
        });
    }

    [Fact]
    public void ReviewDictionaryEditLinkSurvivesTheActualAdvancedGuidedModalHandoff()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.Resources.Add(new() { Id = "extension", Type = "contoso.provider", SensitiveInputs = new() { "opaque" } });
            var controller = new InstallerController(project);
            using var editor = new PackageBuilderForm(controller); editor.Show();
            Exception? failure = null; var clicked = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) =>
            {
                var wizard = Application.OpenForms.OfType<BuilderWizardForm>().FirstOrDefault(form => !form.InvokeRequired &&
                    ReferenceEquals(typeof(BuilderWizardForm).GetField("_advanced", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form), editor));
                if (wizard is null) return;
                timer.Stop();
                try
                {
                    typeof(BuilderWizardForm).GetMethod("GoTo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(wizard, new object[] { 5 });
                    var review = Descendants(wizard).OfType<GuidedReviewPage>().Single();
                    UiStaTest.Pump(review.PendingCheck!);
                    var diagnostic = review.Diagnostics.Single(item => item.Code == "BI1703");
                    Descendants(review).OfType<Button>().Single(button => Equals(button.Tag, diagnostic.Path)).PerformClick(); clicked = true;
                }
                catch (Exception ex) { failure = ex; wizard.Close(); }
            };
            timer.Start();
            editor.GetType().GetMethod("ShowGuided", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(editor, new object[] { false });
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Application.DoEvents(); clicked.Should().BeTrue(); editor.Visible.Should().BeTrue(); editor.ActiveSectionId.Should().Be("resources");
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("opaque"); properties.ContainsFocus.Should().BeTrue();
            properties.SelectedGridItem.PropertyDescriptor.SetValue(properties.SelectedObject, "secret://env/CONTOSO_INPUT");
            controller.Project.Should().BeSameAs(project); project.Resources.Single().Inputs["opaque"].Should().Be("secret://env/CONTOSO_INPUT");
        });
    }

    [Fact]
    public void HiddenAdvancedEditorDefersDiagnosticFocusUntilItBecomesVisible()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.Resources.Add(new() { Id = "extension", Type = "contoso.provider", SensitiveInputs = new() { "opaque" } });
            using var editor = new PackageBuilderForm(new InstallerController(project));
            editor.OpenDiagnostic("Resources[0].Inputs.opaque"); editor.Show(); Application.DoEvents();
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("opaque"); properties.ContainsFocus.Should().BeTrue();
            editor.Hide(); editor.OpenDiagnostic("Resources[0].Inputs.server"); editor.Show(); Application.DoEvents();
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("server"); properties.ContainsFocus.Should().BeTrue();
            project.Resources.Single().Inputs.Should().BeEmpty("merely focusing absent inputs does not add them");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredFocusIsDiscardedWhenItsSectionOrProjectIsReplaced(bool replaceProject)
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.Resources.Add(new() { Id = "extension", Type = "contoso.provider" });
            var controller = new InstallerController(project);
            using var editor = new PackageBuilderForm(controller);
            editor.OpenDiagnostic("Resources[0].Inputs.server");
            if (replaceProject) controller.New("Replacement", "2.0.0", "Publisher", "");
            else editor.OpenSection("iissites");
            editor.Show(); Application.DoEvents();
            var properties = Descendants(editor.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedObject.Should().BeNull("the replacement project and section have no resource rows");
            project.Resources.Single().Inputs.Should().BeEmpty();
        });
    }

    [Fact]
    public void ReviewResourcesHaveSpecificEditTargetsAndBoundedItemPreviews()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            for (var index = 0; index < 8; index++) project.IisSites.Add(new IisSiteDefinition { Name = "Site " + index });
            project.CustomActions.Add(new TheTechIdea.Beep.Installer.Steps.CustomAction { Path = "task.exe", Arguments = "secret-password" });
            var component = new InstallComponent { Id = "core", Name = "Core" };
            component.Files.Add(new FileCopyOperation { SourcePath = "app.exe", DestinationPath = "app.exe" });
            project.Components.Add(component);
            var edited = new List<string>();
            using var host = new Form();
            using var review = new GuidedReviewPage(project, edited.Add, () => { });
            host.Controls.Add(review);
            host.Show();
            Descendants(review).OfType<Button>().Single(b => Equals(b.Tag, "IisSites[3]")).PerformClick();
            edited.Should().Equal("IisSites[3]");
            Descendants(review).OfType<Button>().Should().Contain(b => Equals(b.Tag, "Components[0].Files[0]"));
            Descendants(review).OfType<Button>().Should().Contain(b => Equals(b.Tag, "CustomActions[0]"));
            Descendants(review).OfType<Button>().Should().NotContain(b => Equals(b.Tag, "IisSites[5]"));
            Descendants(review).OfType<Label>().Should().NotContain(label => label.Text.Contains("secret-password"));
        });
    }

    [Fact]
    public void CustomActionDraftChangesAreAppliedExplicitlyAndCancelPreservesTheOriginal()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.CustomActions.Add(new TheTechIdea.Beep.Installer.Steps.CustomAction { Path = "original.exe", Arguments = "/keep" });
            using var dialog = new CustomActionsDialog(project.CustomActions);
            dialog.Show();
            DiagnosticEditorNavigator.Focus(dialog, "CustomActions[0].Path");
            var path = Descendants(dialog).OfType<TextBox>().Single(box => box.DataBindings.Cast<Binding>()
                .Any(binding => binding.BindingMemberInfo.BindingField == "Path"));
            path.Text = "edited.exe";
            project.CustomActions[0].Path.Should().Be("original.exe");
            dialog.ResultActions[0].Path.Should().Be("edited.exe");
            dialog.ResultActions[0].Arguments.Should().Be("/keep");
            using var editor = new PackageBuilderForm(new InstallerController(project));
            var added = new TheTechIdea.Beep.Installer.Steps.CustomAction { Path = "added.exe", TimeoutMs = 1200 };
            editor.ApplyCustomActionResult(dialog.ResultActions.Append(added));
            project.CustomActions.Should().HaveCount(2);
            project.CustomActions[0].Path.Should().Be("edited.exe");
            project.CustomActions[1].Should().BeSameAs(added);
            project.IsDirty.Should().BeTrue();
        });
    }

    [Fact]
    public void ReopeningProjectsAfterDisposingCachedBindingsClearsResultsAndBindsOnlyTheCurrentProject()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var editor = new PackageBuilderForm(controller);
            editor.Show();
            editor.OpenSection("components");
            editor.RecordLastBuild(new BuildPipeline.BuildResult { Success = true, OutputFile = "old.exe" });
            controller.New("Second", "2.0.0", "Publisher", "");
            editor.LastBuildResult.Should().BeNull();
            editor.OpenSection("identity");
            controller.New("Third", "3.0.0", "Publisher", "");
            editor.OpenSection("components");
            var grid = Descendants(editor.ActiveSection!).OfType<DataGridView>().Single();
            ((BindingSource)grid.DataSource!).DataSource.Should().BeSameAs(controller.Project.Components);
            editor.Project.AppName.Should().Be("Third");
        });
    }

    [Fact]
    public void ResourceChangesInvalidateReadinessEvenWhenTheProjectWasAlreadyDirty()
    {
        RunSta(() =>
        {
            var controller = new InstallerController();
            using var editor = new PackageBuilderForm(controller);
            editor.Show();
            editor.Project.MarkDirty();
            editor.OpenSection("prerequisites");
            var checks = (LatestProjectCheck)typeof(PackageBuilderForm)
                .GetField("_readinessChecks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
            var add = Descendants(editor.ActiveSection!).OfType<Button>().Single(b => b.Text == "Add");
            add.PerformClick();
            var first = checks.Generation;
            add.PerformClick();
            checks.Generation.Should().BeGreaterThan(first);
            editor.Project.Prerequisites.Should().HaveCount(2);
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
