using System;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The section menu groups its forty items into eight collapsible headers, but every group starts
/// open: collapsing all but the current one forced browsing them one at a time, which is worse than
/// the flat list it replaced. Collapsing is a per-header opt-out, not the default.
/// </summary>
public sealed class LeftNavGroupingTests
{
    private static LeftNavPanel BuiltNav()
    {
        var nav = new LeftNavPanel();

        var product = nav.AddSection("product", "Product");
        nav.AddItem(product, "identity", "Identity", "Name, version, publisher");
        nav.AddItem(product, "source", "Source", "Where the files come from");

        var deployment = nav.AddSection("deployment", "Deployment");
        nav.AddItem(deployment, "shortcuts", "Shortcuts", "Start menu and desktop");
        nav.AddItem(deployment, "registry", "Registry", "Keys written at install");

        var output = nav.AddSection("output", "Output");
        nav.AddItem(output, "compression", "Compression", "Method and level");

        return nav;
    }

    private static ListView List(LeftNavPanel nav)
        => nav.Controls.OfType<ListView>().Single();

    [Fact]
    public void GroupsAreCollapsible()
    {
        using var nav = BuiltNav();

        var groups = List(nav).Groups.Cast<ListViewGroup>().ToList();

        groups.Should().NotBeEmpty();
        groups.Should().OnlyContain(g => g.CollapsedState != ListViewGroupCollapsedState.Default,
            "a group that is not collapsible leaves the whole list on screen at once");
    }

    [Fact]
    public void AllGroupsStartOpen()
    {
        // Collapsing every group but the current one forces the user to open the rest by hand, one
        // at a time, just to see what's in them. Groups start open; collapsing is opt-in.
        using var nav = BuiltNav();

        var groups = List(nav).Groups.Cast<ListViewGroup>().ToList();
        groups.Should().NotBeEmpty();
        groups.Should().OnlyContain(g => g.CollapsedState == ListViewGroupCollapsedState.Expanded,
            "the menu should open with every group visible, not one at a time");
    }

    [Fact]
    public void SelectingASectionOpensTheGroupThatHoldsIt()
    {
        // A collapsed group hides its rows. Selecting into one without expanding it would scroll to
        // a row the user cannot see -- the section changes and the menu looks like it did nothing.
        using var nav = BuiltNav();

        nav.SelectSection("compression");

        var owner = List(nav).Groups.Cast<ListViewGroup>().Single(g => g.Name == "output");
        owner.CollapsedState.Should().Be(ListViewGroupCollapsedState.Expanded);
        nav.SelectedSectionId.Should().Be("compression");
    }

    [Fact]
    public void FilteringOpensEveryGroupThatStillHasMatches()
    {
        // The point of typing is to see the matches; leaving them collapsed would hide the results
        // of the search.
        using var nav = BuiltNav();

        nav.FilterText = "e";       // matches items across more than one group

        var groups = List(nav).Groups.Cast<ListViewGroup>().ToList();
        groups.Should().NotBeEmpty();
        groups.Should().OnlyContain(g => g.CollapsedState == ListViewGroupCollapsedState.Expanded,
            "search results must be visible without expanding anything by hand");
    }

    [Fact]
    public void ClearingTheFilterKeepsEveryGroupOpen()
    {
        using var nav = BuiltNav();
        nav.SelectSection("identity");
        nav.FilterText = "e";
        nav.FilterText = "";

        var groups = List(nav).Groups.Cast<ListViewGroup>().ToList();
        groups.Should().OnlyContain(g => g.CollapsedState == ListViewGroupCollapsedState.Expanded,
            "clearing the search returns to the default menu, which is fully open");
    }

    [Fact]
    public void EveryItemIsStillReachable()
    {
        // Collapsing is presentation. It must not remove anything from the model the builder walks.
        using var nav = BuiltNav();

        nav.VisibleSectionIds.Should().BeEquivalentTo(new[]
        {
            "identity", "source", "shortcuts", "registry", "compression",
        });
    }
}
