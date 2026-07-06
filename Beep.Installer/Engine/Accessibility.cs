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
            if (string.IsNullOrWhiteSpace(c.Text)) return;
            c.AccessibleName = StripMnemonic(c.Text);
            count++;
        });
        return count;
    }

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

    internal static bool IsInteractive(Control c)
        => c is Button or TextBox or CheckBox or RadioButton
            or ComboBox or NumericUpDown or ListBox or TreeView or LinkLabel;

    internal static bool IsTextEntry(Control c) => c is TextBox or ComboBox or NumericUpDown or ListBox or TreeView;

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
