using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// ComponentConditionsDialog and CustomActionsDialog each hand-rolled "count errors, count the rest
/// as warnings, set a label's text and color" against differently shaped issue types
/// (ConditionListValidator.Issue's Severity enum vs CustomActionIssue's bool IsError). ValidationCounts
/// is the extracted, shared version; these tests cover the counting and label-formatting it replaced,
/// independent of either dialog.
/// </summary>
public sealed class ValidationCountsTests
{
    private sealed record BoolIssue(bool IsError);

    [Fact]
    public void CountsErrorsAndTreatsEverythingElseAsWarning()
    {
        var issues = new[] { new BoolIssue(true), new BoolIssue(false), new BoolIssue(false) };

        var counts = ValidationCounts.From(issues, i => i.IsError);

        counts.Errors.Should().Be(1);
        counts.Warnings.Should().Be(2);
        counts.IsClean.Should().BeFalse();
    }

    [Fact]
    public void NoIssuesIsClean()
    {
        var counts = ValidationCounts.From(System.Array.Empty<BoolIssue>(), i => i.IsError);

        counts.IsClean.Should().BeTrue();
        counts.Errors.Should().Be(0);
        counts.Warnings.Should().Be(0);
    }

    [Fact]
    public void ApplyShowsTheOkSuffixWhenClean()
    {
        using var label = new Label();

        ValidationCounts.From(System.Array.Empty<BoolIssue>(), i => i.IsError)
            .Apply(label, "3 action(s) -", "OK");

        label.Text.Should().Be("3 action(s) - OK");
        label.ForeColor.Should().Be(Color.DarkGreen);
    }

    [Fact]
    public void ApplyShowsCountsAndRedWhenThereAreErrors()
    {
        using var label = new Label();
        var issues = new[] { new BoolIssue(true), new BoolIssue(false) };

        ValidationCounts.From(issues, i => i.IsError).Apply(label, "2 action(s) -", "OK");

        label.Text.Should().Be("2 action(s) - 1 error(s), 1 warning(s).");
        label.ForeColor.Should().Be(Color.DarkRed);
    }

    [Fact]
    public void ApplyShowsOrangeWhenOnlyWarnings()
    {
        using var label = new Label();
        var issues = new[] { new BoolIssue(false) };

        ValidationCounts.From(issues, i => i.IsError).Apply(label, "1 action(s) -", "OK");

        label.Text.Should().Be("1 action(s) - 0 error(s), 1 warning(s).");
        label.ForeColor.Should().Be(Color.DarkOrange);
    }
}
