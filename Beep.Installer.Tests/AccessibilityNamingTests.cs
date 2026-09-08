using System.Linq;
using System.Windows.Forms;
using A11y = Beep.Installer.Engine.Accessibility;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Accessible names for the controls that cannot name themselves (7.C.1).
///
/// <c>ApplyAutoNames</c> derived the name from <c>Control.Text</c>, which works for a button or a
/// checkbox — their Text *is* their caption. It is exactly wrong for the fields that matter: a text
/// box or combo box is captioned by a separate <see cref="Label"/>, and its own Text is the value
/// the user typed. So every field in the Package Builder — 54 of them — was announced by a screen
/// reader as a bare "edit", and naming from Text would have been worse still, echoing the value
/// back as the field's name and changing it on every keystroke.
/// </summary>
public class AccessibilityNamingTests
{
    private static TableLayoutPanel TwoColumnRow(string caption, Control field)
    {
        var table = new TableLayoutPanel { ColumnCount = 2, RowCount = 1 };
        var label = new Label { Text = caption };
        table.Controls.Add(label, 0, 0);
        table.Controls.Add(field, 1, 0);
        return table;
    }

    [Fact]
    public void AFieldIsNamedByTheLabelInItsRow()
    {
        var box = new TextBox();
        using var table = TwoColumnRow("Sidebar text:", box);

        A11y.ApplyAutoNames(table);

        box.AccessibleName.Should().Be("Sidebar text", "the caption names the field, minus its colon");
    }

    [Fact]
    public void AFieldsOwnTextIsNeverUsedAsItsName()
    {
        // The regression this guards: naming a text box from its Text makes the accessible name the
        // user's data, so it changes as they type and never says what the field is for.
        var box = new TextBox { Text = "C:\\Program Files\\MyApp" };
        using var table = TwoColumnRow("Install folder:", box);

        A11y.ApplyAutoNames(table);

        box.AccessibleName.Should().Be("Install folder");
    }

    [Fact]
    public void AControlThatCaptionsItselfKeepsUsingItsOwnText()
    {
        using var panel = new Panel();
        var button = new Button { Text = "&Browse…" };
        panel.Controls.Add(button);

        A11y.ApplyAutoNames(panel);

        button.AccessibleName.Should().Be("Browse…", "mnemonics are not spoken");
    }

    [Fact]
    public void AnExplicitNameIsNeverOverwritten()
    {
        var box = new TextBox { AccessibleName = "Product version, semantic" };
        using var table = TwoColumnRow("Version:", box);

        A11y.ApplyAutoNames(table);

        box.AccessibleName.Should().Be("Product version, semantic");
    }

    [Fact]
    public void OnlyTheLabelToTheLeftInTheSameRowCounts()
    {
        // A two-row table: naming must not reach into the neighbouring row.
        var table = new TableLayoutPanel { ColumnCount = 2, RowCount = 2 };
        var first = new TextBox();
        var second = new TextBox();
        table.Controls.Add(new Label { Text = "Publisher:" }, 0, 0);
        table.Controls.Add(first, 1, 0);
        table.Controls.Add(new Label { Text = "Version:" }, 0, 1);
        table.Controls.Add(second, 1, 1);

        using (table)
        {
            A11y.ApplyAutoNames(table);

            first.AccessibleName.Should().Be("Publisher");
            second.AccessibleName.Should().Be("Version");
        }
    }

    [Fact]
    public void OutsideATableTheNearestLabelOnTheSameLineCaptionsTheField()
    {
        using var panel = new Panel();
        var box = new TextBox { Bounds = new System.Drawing.Rectangle(120, 10, 100, 20) };
        panel.Controls.Add(new Label { Text = "Feed URL:", Bounds = new System.Drawing.Rectangle(10, 10, 100, 20) });
        panel.Controls.Add(new Label { Text = "Not this one:", Bounds = new System.Drawing.Rectangle(10, 60, 100, 20) });
        panel.Controls.Add(box);

        A11y.ApplyAutoNames(panel);

        box.AccessibleName.Should().Be("Feed URL");
    }

    [Fact]
    public void ACheckedListBoxIsTreatedAsInteractive()
    {
        // The [WizardPages] checklist is a CheckedListBox. It was in neither the naming set nor the
        // tab-stop set, so it was skipped entirely.
        var list = new CheckedListBox();
        using var table = TwoColumnRow("Wizard pages:", list);

        A11y.ApplyAutoNames(table);

        A11y.IsInteractive(list).Should().BeTrue();
        list.AccessibleName.Should().Be("Wizard pages");
    }

    [Theory]
    [InlineData(typeof(DateTimePicker))]
    [InlineData(typeof(TrackBar))]
    [InlineData(typeof(ListView))]
    [InlineData(typeof(DataGridView))]
    public void ControlTypesThatUsedToBeSkippedAreCoveredNow(System.Type controlType)
    {
        using var control = (Control)System.Activator.CreateInstance(controlType)!;

        A11y.IsInteractive(control).Should().BeTrue();
    }

    [Fact]
    public void AFieldWithNoCaptionIsLeftAloneRatherThanGivenAWrongName()
    {
        using var panel = new Panel();
        var box = new TextBox();
        panel.Controls.Add(box);

        A11y.ApplyAutoNames(panel);

        box.AccessibleName.Should().BeNullOrEmpty("a guessed name is worse than none");
    }

    [Fact]
    public void LazilyAddedPanelsAreNamedToo()
    {
        // The Package Builder creates each section the first time it is shown, long after the form
        // is constructed. A one-shot pass at construction missed everything the user edits.
        using var form = new Form();
        A11y.Attach(form);

        var panel = new Panel();
        var box = new TextBox();
        var table = TwoColumnRow("Added later:", box);
        panel.Controls.Add(table);
        form.Controls.Add(panel);

        box.AccessibleName.Should().Be("Added later");
    }
}
