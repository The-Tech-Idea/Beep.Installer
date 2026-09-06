using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class PackageBuilderAuthoringUxTests
{
    [Fact]
    public void IdentityEditor_BindsPersistentAppIdAndLabelsItAccessibly()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("IdentityApp", "1.0.0", "ACME", "");
            using var form = new PackageBuilderForm(new InstallerController(project));
            PrivateField<LeftNavPanel>(form, "_nav").SelectSection("identity");
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            var field = form.Controls.Find(nameof(InstallProject.AppId), true)
                .Should().ContainSingle().Subject.Should().BeOfType<TextBox>().Subject;
            field.AccessibleName.Should().Be("Product ID (AppId)");
            field.TabStop.Should().BeTrue();
            var binding = field.DataBindings["Text"]!;
            binding.DataSource.Should().BeSameAs(project);
            binding.ReadValue();
            field.Text.Should().Be(project.AppId);
            var changed = Guid.NewGuid().ToString("D");
            field.Text = changed;
            binding.WriteValue();
            project.AppId.Should().Be(changed);
        });
    }

    [Fact]
    public void PackageBuilder_Exposes_Searchable_AdvancedResourceEditors()
    {
        RunSta(() =>
        {
            using var form = new PackageBuilderForm(new InstallerController(
                InstallerProjectFactory.CreateNew("AuthoringApp", "1.0.0", "The Tech Idea", "")));

            var nav = PrivateField<LeftNavPanel>(form, "_nav");
            nav.VisibleSectionIds.Should().Contain(new[]
            {
                "scheduledtasks",
                "firewallrules",
                "certificates",
                "comregistrations",
                "driverpackages",
                "configtransforms",
                "iisapppools",
                "iissites",
                "webdeploy"
            });

            nav.FilterText = "drivers";
            nav.VisibleSectionIds.Should().Contain("driverpackages");
            nav.VisibleSectionIds.Should().NotContain("identity");

            nav.FilterText = "";
            nav.SelectSection("driverpackages");
            var contentHost = PrivateField<Panel>(form, "_contentHost");
            contentHost.Controls.Count.Should().Be(1);

            nav.SelectSection("build");
            PrivateField<TextBox>(form, "_validationCenterBox")
                .Text.Should().Contain("Validation Center", because: "the Build Workflow hosts the grouped validation-center pane");

            var refresh = form.Controls
                .Find("RefreshFormatReadinessButton", searchAllChildren: true)
                .Should().ContainSingle().Subject
                .Should().BeAssignableTo<Button>().Subject;
            refresh.PerformClick();
            PrivateField<TextBox>(form, "_formatCapabilityBox")
                .Text.Should().Contain("Release readiness:", because: "the Build Workflow should summarize release readiness without requiring raw artifact inspection");

            var keyboardGuide = PrivateField<TextBox>(form, "_keyboardWalkthroughBox").Text;
            keyboardGuide.Should().Contain("Ctrl+F");
            keyboardGuide.Should().Contain("Ctrl+Tab");
            keyboardGuide.Should().Contain("F7");
            keyboardGuide.Should().Contain("F5");
        });
    }

    [Fact]
    public void LeftNav_Supports_KeyboardAdjacentSelection()
    {
        RunSta(() =>
        {
            using var nav = new LeftNavPanel();
            var group = nav.AddSection("project", "Project");
            nav.AddItem(group, "identity", "Identity");
            nav.AddItem(group, "layout", "Layout");
            nav.AddItem(group, "build", "Build");

            nav.SelectSection("identity");
            nav.SelectedSectionId.Should().Be("identity");

            nav.SelectAdjacentSection(+1).Should().BeTrue();
            nav.SelectedSectionId.Should().Be("layout");

            nav.SelectAdjacentSection(-1).Should().BeTrue();
            nav.SelectedSectionId.Should().Be("identity");
        });
    }

    private static T PrivateField<T>(object owner, string name) where T : class
        => typeof(PackageBuilderForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)
            .Should().BeAssignableTo<T>().Subject;

    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }
}
