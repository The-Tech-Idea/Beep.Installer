using System.Collections.ObjectModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ResourceSectionPresentationTests : IDisposable
{
    private readonly string _original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
    public void Dispose() => LanguageManager.SetLanguage(_original);

    [Fact]
    public void EmptyStateTracksAddAndRemoveAndThePanelOwnsItsBinding()
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage("en");
            var resources = new ObservableCollection<RegistryOperation>();
            using var host = new Form { ClientSize = new Size(780, 460) };
            using var view = ResourceSectionFactory.Create("Registry", "Registry entries created during installation.", resources,
                () => new RegistryOperation { KeyPath = "Software\\Contoso" }, () => { },
                item => new RegistryOperation { KeyPath = item.KeyPath });
            host.Controls.Add(view);
            host.Show();
            Application.DoEvents();
            var empty = Descendants(view).Single(c => c.Name == "ResourceEmpty");
            var grid = Descendants(view).OfType<DataGridView>().Single();
            var binding = (BindingSource)grid.DataSource!;
            var disposed = false;
            binding.Disposed += (_, _) => disposed = true;
            empty.Visible.Should().BeTrue();
            Descendants(view).OfType<Button>().Single(b => b.Text == "Add").PerformClick();
            Application.DoEvents();
            resources.Should().HaveCount(1);
            empty.Visible.Should().BeFalse();
            grid.Visible.Should().BeTrue();
            grid.Rows.Count.Should().Be(1);
            Descendants(view).OfType<Button>().Single(b => b.Text == "Remove").PerformClick();
            Application.DoEvents();
            resources.Should().BeEmpty();
            empty.Visible.Should().BeTrue();
            view.Dispose();
            disposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("de", 1f)]
    [InlineData("de", 1.25f)]
    [InlineData("de", 1.5f)]
    [InlineData("de", 2f)]
    [InlineData("ar", 1f)]
    [InlineData("ar", 1.25f)]
    [InlineData("ar", 1.5f)]
    [InlineData("ar", 2f)]
    public void ResourceHelpAndActionsRemainVisibleWithLongTextAtSupportedScales(string culture, float scale)
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage(culture);
            var resources = new ObservableCollection<RegistryOperation> { new() { KeyPath = "Software\\Contoso" } };
            using var host = new Form { ClientSize = new Size(780, 460) };
            using var view = ResourceSectionFactory.Create(LanguageManager.GetString("Section_Registry"),
                LanguageManager.GetString("Guided_OptionalBehavior"), resources, () => new RegistryOperation(), () => { }, item => item);
            host.Controls.Add(view);
            host.Show();
            host.Scale(new SizeF(scale, scale));
            Beep.Installer.Engine.RtlHelper.ApplyDirection(host, culture == "ar");
            Application.DoEvents();
            host.PerformLayout();
            var help = Descendants(view).OfType<Label>().Single(c => c.Name == "SectionHelp");
            help.Height.Should().BeGreaterThanOrEqualTo(help.GetPreferredSize(new Size(help.Width, 0)).Height);
            foreach (var button in Descendants(view).OfType<Button>())
            {
                var bounds = host.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                host.ClientRectangle.Contains(bounds).Should().BeTrue($"'{button.Text}' must fit at {culture}/{scale}");
            }
            Descendants(view).OfType<DataGridView>().Single().Rows.Count.Should().Be(1);
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
