using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Models;
using Beep.Installer.Pages;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// A page that knows it is incomplete says so before the user presses Next.
///
/// Two defects, one shape. Every page declares <c>ValidityChanged</c> and most of them raise it —
/// but only <see cref="LicensePage"/> was ever subscribed by the host, so FolderPage, StartMenuPage,
/// PrerequisitePage, ComponentSelectionPage and AdditionalTasksPage were raising into the void: the
/// Next button kept whatever <c>CanGoNext</c> happened to say when the page was navigated to.
///
/// <see cref="CustomPage"/> had the mirror problem — it declared the event, never raised it, and
/// hard-coded <c>CanGoNext =&gt; true</c>, so a page with a required field left Next enabled and
/// refused the click with a modal. In both cases the failure landed somewhere other than the mistake.
/// </summary>
[Collection("Language")]
public class PageValidityTests
{
    private static InstallContext Context() => new()
    {
        Project = new InstallProject { AppName = "Contoso", AppVersion = "1.0.0" },
        InstallPath = Path.Combine(Path.GetTempPath(), "Contoso"),
        PerUser = false,
    };

    private static CustomWizardPage PageWithRequiredField() => new()
    {
        Id = "license-key",
        Title = "Licensing",
        Fields =
        {
            new CustomField { Id = "key", Label = "Licence key", Type = CustomFieldType.Text, Required = true },
        },
    };

    private static TextBox FirstTextBox(Control root)
        => Descendants(root).OfType<TextBox>().First();

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    [Fact]
    public void ACustomPageWithAnUnansweredRequiredFieldIsNotReadyToAdvance()
    {
        using var page = new CustomPage(PageWithRequiredField(), Context());

        page.CanGoNext.Should().BeFalse("the required licence key has not been entered");
    }

    [Fact]
    public void AnsweringTheRequiredFieldMakesThePageReady()
    {
        using var page = new CustomPage(PageWithRequiredField(), Context());

        FirstTextBox(page).Text = "ABC-123";

        page.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void ThePageAnnouncesThatItBecameValid()
    {
        // The host listens to this to enable Next. Computing CanGoNext correctly but never raising
        // the event would leave the button stale until the next navigation.
        using var page = new CustomPage(PageWithRequiredField(), Context());

        var announced = new List<bool>();
        page.ValidityChanged += (_, ok) => announced.Add(ok);

        FirstTextBox(page).Text = "ABC-123";

        announced.Should().Equal(new[] { true }, "becoming valid is exactly one transition");
    }

    [Fact]
    public void ThePageAnnouncesThatItBecameInvalidAgain()
    {
        using var page = new CustomPage(PageWithRequiredField(), Context());
        var box = FirstTextBox(page);
        box.Text = "ABC-123";

        var announced = new List<bool>();
        page.ValidityChanged += (_, ok) => announced.Add(ok);

        box.Text = "";

        announced.Should().Equal(new[] { false });
    }

    [Fact]
    public void TheReasonIsOnScreenRatherThanOnlyInAModal()
    {
        // The point of the change: the problem is visible where it was made, not after a click.
        using var page = new CustomPage(PageWithRequiredField(), Context());

        var shown = Descendants(page).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0).ToList();

        shown.Should().NotBeEmpty("an unanswered required field must explain itself in the page");
    }

    [Fact]
    public void APageWithNoRequiredFieldsIsReadyImmediately()
    {
        var optional = new CustomWizardPage
        {
            Id = "options",
            Fields = { new CustomField { Id = "note", Label = "Note", Type = CustomFieldType.Text } },
        };

        using var page = new CustomPage(optional, Context());

        page.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void EveryPageThatReportsValidityIsSubscribedByTheHost()
    {
        // Source-level, because building the wizard needs a live install context and a message loop.
        // The old code named one page type; anything added later silently went unheard.
        var source = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "BeepModernInstallerForm.cs"));

        var wiring = Between(source, "foreach (var page in _pages)", "private void AddPageIfEnabled");

        wiring.Should().Contain("ValidityChanged",
            "the host must subscribe to page validity");
        wiring.Should().Contain("_nextBtn.Enabled = ok",
            "the subscription must actually drive the Next button");
        wiring.Should().Contain("_pages[_currentPage], subject",
            "only the page on screen may drive the button");

        source.Should().NotContain("OfType<LicensePage>().FirstOrDefault() is { } lic",
            "subscribing one named page type is the defect this replaced");
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, $"'{start}' should exist");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(-1, $"'{end}' should follow '{start}'");
        return source[from..to];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }
}
