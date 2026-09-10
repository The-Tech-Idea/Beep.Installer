using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Beep.Installer.Ui;

/// <summary>
/// "N error(s), M warning(s)" label text + color, computed from any validator's issue list.
///
/// <c>ComponentConditionsDialog</c> (<c>ConditionListValidator.Issue</c>, a three-level
/// <c>Severity</c> enum) and <c>CustomActionsDialog</c> (<c>CustomActionIssue</c>, a bool
/// <c>IsError</c>) each hand-rolled the same three lines -- count errors, count the rest as
/// warnings, set a label's text and color from the counts -- against two differently shaped issue
/// types. This takes an <c>isError</c> predicate instead of assuming a shared issue interface, so a
/// third validator's issue type needs only that predicate to use it, not a rename or a new base type.
/// </summary>
public readonly struct ValidationCounts
{
    public int Errors { get; }
    public int Warnings { get; }
    public bool IsClean => Errors == 0 && Warnings == 0;

    public ValidationCounts(int errors, int warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    /// <summary>Counts a validator's issues. Everything that is not an error is a warning, matching
    /// what every site already assumed (<c>warnings = issues.Count - errors</c>).</summary>
    public static ValidationCounts From<TIssue>(IEnumerable<TIssue> issues, Func<TIssue, bool> isError)
    {
        var list = issues as ICollection<TIssue> ?? issues.ToList();
        var errors = list.Count(isError);
        return new ValidationCounts(errors, list.Count - errors);
    }

    /// <summary>
    /// Sets a label's text and color the way every dialog above did by hand: <paramref name="subject"/>
    /// plus either <paramref name="okSuffix"/> when clean, or the error/warning counts.
    /// </summary>
    public void Apply(Label label, string subject, string okSuffix)
    {
        label.Text = IsClean
            ? $"{subject} {okSuffix}"
            : $"{subject} {Errors} error(s), {Warnings} warning(s).";
        label.ForeColor = Errors > 0 ? Color.DarkRed : Warnings > 0 ? Color.DarkOrange : Color.DarkGreen;
    }
}
