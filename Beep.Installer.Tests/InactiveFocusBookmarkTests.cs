using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class InactiveFocusBookmarkTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void InactiveTextTargetAndSelectionAreRetainedWithoutReactivatingItsWindow()
    {
        Sta(() =>
        {
            using var owner = new Form(); using var other = new Form();
            using var field = new TextBox { Text = "Authored selection" }; using var otherField = new TextBox();
            owner.Controls.Add(field); other.Controls.Add(otherField); owner.Show(); Application.DoEvents(); owner.Activate(); field.Focus(); field.Select(3, 6);
            var bookmark = UiFocusBookmark.Capture(owner); bookmark.Should().NotBeNull();
            owner.RightToLeft = RightToLeft.Yes; owner.RightToLeftLayout = true;
            other.Show(); other.Activate(); otherField.Focus(); Application.DoEvents(); otherField.Focused.Should().BeTrue();
            owner.ActiveControl = null; field.Select(0, 0);
            bookmark!.Restore(); Application.DoEvents();
            otherField.Focused.Should().BeTrue(); owner.ContainsFocus.Should().BeFalse(); owner.ActiveControl.Should().BeSameAs(field);
            field.SelectionStart.Should().Be(3); field.SelectionLength.Should().Be(6); field.Text.Should().Be("Authored selection");
            owner.Activate(); Application.DoEvents(); field.Focused.Should().BeTrue();
            field.SelectionStart.Should().Be(3); field.SelectionLength.Should().Be(6);
        });
    }

    [Fact]
    public void NestedGridTargetReturnsOnActivationWithoutChangingSelectedData()
    {
        Sta(() =>
        {
            using var owner = new Form(); using var other = new Form(); using var host = new SplitContainer { Dock = DockStyle.Fill };
            using var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false }; grid.Columns.Add("Name", "Name"); grid.Rows.Add("First"); grid.Rows.Add("Selected");
            using var otherField = new TextBox(); host.Panel1.Controls.Add(grid); owner.Controls.Add(host); other.Controls.Add(otherField);
            owner.Show(); Application.DoEvents(); owner.Activate(); grid.CurrentCell = grid.Rows[1].Cells[0]; grid.Focus();
            var bookmark = UiFocusBookmark.Capture(owner); bookmark.Should().NotBeNull();
            owner.RightToLeft = RightToLeft.Yes; owner.RightToLeftLayout = true;
            other.Show(); other.Activate(); otherField.Focus(); Application.DoEvents();
            owner.ActiveControl = null; bookmark!.Restore(); Application.DoEvents();
            otherField.Focused.Should().BeTrue(); owner.ContainsFocus.Should().BeFalse(); owner.ActiveControl.Should().NotBeNull();
            grid.CurrentCell.Should().BeSameAs(grid.Rows[1].Cells[0]);
            owner.Activate(); Application.DoEvents(); grid.Focused.Should().BeTrue(); grid.CurrentCell.Should().BeSameAs(grid.Rows[1].Cells[0]);
            grid.Rows[1].Cells[0].Value.Should().Be("Selected");
        });
    }
}
