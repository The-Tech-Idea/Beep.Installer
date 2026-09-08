using System;
using System.Text;
using System.Windows.Forms;

namespace Beep.Installer.Engine;

/// <summary>
/// Accessibility helpers (Track X2). Auto-fills <see cref="Control.AccessibleName"/> from
/// <see cref="Control.Text"/> (mnemonics stripped) for interactive controls, normalizes tab order,
/// and (when the OS is in High Contrast mode) re-colors the UI with system colors so it stays legible.
/// UI-tree logic kept pure so it is unit-testable against a constructed control tree.
/// </summary>
public static class Accessibility
{
    /// <summary>
    /// Sets <see cref="Control.AccessibleName"/> from <see cref="Control.Text"/> (mnemonics stripped)
    /// for interactive controls that don't already have one. Returns how many names were assigned.
    /// </summary>
    public static int ApplyAutoNames(Control root)
    {
        int count = 0;
        Walk(root, c =>
        {
            if (!IsInteractive(c)) return;
            if (!string.IsNullOrEmpty(c.AccessibleName)) return;

            var name = ResolveName(c);
            if (string.IsNullOrWhiteSpace(name)) return;

            c.AccessibleName = name;
            count++;
        });
        return count;
    }

    /// <summary>
    /// The name a screen reader should announce for <paramref name="control"/>.
    ///
    /// A button or a checkbox carries its own <see cref="Control.Text"/>, so it names itself. The
    /// controls that matter most do not: a text box, a combo box or a list is captioned by a
    /// separate <see cref="Label"/> beside it, and its own Text is the *value*, not the label.
    /// Naming from Text alone therefore skipped every one of them — the Package Builder has 54
    /// label+field rows, and a screen reader announced each field as a bare "edit".
    /// </summary>
    internal static string ResolveName(Control control)
    {
        // A control whose Text is its caption (button, checkbox, radio, link) names itself. For a
        // text entry the Text is user data, and echoing it back as the field's name is worse than
        // useless -- it changes as the user types.
        if (!IsTextEntry(control) && !string.IsNullOrWhiteSpace(control.Text))
            return StripMnemonic(control.Text);

        return FindCaptionLabel(control) is { } label ? StripCaption(label.Text) : "";
    }

    /// <summary>
    /// Finds the <see cref="Label"/> that captions <paramref name="control"/>.
    ///
    /// The builder lays fields out as two-column <see cref="TableLayoutPanel"/> rows — caption in
    /// column 0, field in column 1 — so the row is the reliable association. Where the parent is a
    /// plain panel, fall back to geometry: the nearest label that sits to the left of the control
    /// on the same line, which is what "captions it" means visually and is how a sighted user
    /// reads it.
    /// </summary>
    private static Label? FindCaptionLabel(Control control)
    {
        if (control.Parent is TableLayoutPanel table)
        {
            var cell = table.GetCellPosition(control);
            if (cell.Row >= 0)
            {
                Label? best = null;
                var bestColumn = int.MinValue;
                foreach (Control sibling in table.Controls)
                {
                    if (sibling is not Label label) continue;
                    var position = table.GetCellPosition(sibling);
                    // Same row, and to the left of the field: the closest such label is the caption.
                    if (position.Row != cell.Row || position.Column >= cell.Column) continue;
                    if (position.Column <= bestColumn) continue;
                    bestColumn = position.Column;
                    best = label;
                }

                if (best != null) return best;
            }
        }

        if (control.Parent is not { } parent) return null;

        Label? nearest = null;
        var nearestGap = int.MaxValue;
        foreach (Control sibling in parent.Controls)
        {
            if (sibling is not Label label || string.IsNullOrWhiteSpace(label.Text)) continue;
            if (label.Right > control.Left) continue;                    // must be to the left
            if (label.Bottom < control.Top || label.Top > control.Bottom) continue;  // and on the same line

            var gap = control.Left - label.Right;
            if (gap >= nearestGap) continue;
            nearestGap = gap;
            nearest = label;
        }

        return nearest;
    }

    /// <summary>Drops the mnemonic marker and the trailing colon captions conventionally carry.</summary>
    internal static string StripCaption(string text)
        => StripMnemonic(text ?? "").TrimEnd().TrimEnd(':').TrimEnd();

    /// <summary>
    /// Assigns a deterministic per-container <see cref="Control.TabIndex"/> (0..n in child order) so
    /// keyboard navigation follows the visible layout. Returns how many controls were ordered.
    /// </summary>
    public static int NormalizeTabOrder(Control root)
    {
        int touched = 0;
        Normalize(root, ref touched);
        return touched;
    }

    /// <summary>
    /// Runs the accessibility pass when <paramref name="form"/> loads, and again whenever a control
    /// is added to it afterwards.
    ///
    /// A single pass at construction is not enough for this app: the Package Builder builds each
    /// left-nav section lazily the first time it is shown, so everything the user actually edits is
    /// created long after the form is. Those panels were never named or tab-ordered.
    /// </summary>
    public static void Attach(Form form)
    {
        if (form == null) return;

        form.Load += (_, _) => EnsureAccessibility(form);
        form.ControlAdded += OnControlAdded;

        static void OnControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control == null) return;
            ApplyAutoNames(e.Control);
            NormalizeTabOrder(e.Control);
            e.Control.ControlAdded += OnControlAdded;
        }
    }

    /// <summary>Re-colors controls with system colors (High Contrast legibility). Returns count touched.</summary>
    public static int ApplyHighContrastColors(Control root)
    {
        int count = 0;
        Walk(root, c =>
        {
            if (IsTextEntry(c))
            {
                c.BackColor = SystemColors.Window;
                c.ForeColor = SystemColors.WindowText;
            }
            else
            {
                c.BackColor = SystemColors.Control;
                c.ForeColor = SystemColors.ControlText;
            }
            count++;
        });
        return count;
    }

    /// <summary>
    /// One-call accessibility pass for a form: auto-names + tab order, plus high-contrast colors
    /// when the OS is running in High Contrast mode.
    /// </summary>
    public static void EnsureAccessibility(Form form)
    {
        if (form == null) return;
        ApplyAutoNames(form);
        NormalizeTabOrder(form);
        if (SystemInformation.HighContrast)
            ApplyHighContrastColors(form);
    }

    // ── helpers ──

    // CheckedListBox, DateTimePicker, TrackBar, ListView and DataGridView were all missing, so they
    // got neither a name nor a tab stop. The [WizardPages] checklist is a CheckedListBox, which
    // makes it a concrete example rather than a hypothetical one. ListBox derives CheckedListBox,
    // so the pattern below already covers it -- it is named explicitly for the reader.
    internal static bool IsInteractive(Control c)
        => c is Button or TextBox or CheckBox or RadioButton
            or ComboBox or NumericUpDown or ListBox or CheckedListBox or TreeView or LinkLabel
            or DateTimePicker or TrackBar or ListView or DataGridView;

    internal static bool IsTextEntry(Control c)
        => c is TextBox or ComboBox or NumericUpDown or ListBox or CheckedListBox or TreeView
            or DateTimePicker or ListView or DataGridView;

    public static string StripMnemonic(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append('&'); i++; }
                continue; // drop the mnemonic marker
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    private static void Walk(Control c, Action<Control> visit)
    {
        visit(c);
        foreach (Control child in c.Controls)
            Walk(child, visit);
    }

    private static void Normalize(Control c, ref int touched)
    {
        int i = 0;
        foreach (Control child in c.Controls)
        {
            child.TabIndex = i++;
            if (IsInteractive(child)) child.TabStop = true;
            touched++;
            Normalize(child, ref touched);
        }
    }
}
