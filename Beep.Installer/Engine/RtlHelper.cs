using System;
using System.Windows.Forms;

namespace Beep.Installer.Engine;

/// <summary>
/// Applies right-to-left layout for Arabic and other RTL languages.
/// </summary>
public static class RtlHelper
{
    /// <summary>Returns true if the given culture code should use RTL layout.</summary>
    public static bool IsRtl(string twoLetterCode)
    {
        return twoLetterCode switch
        {
            "ar" => true, // Arabic
            "he" => true, // Hebrew
            "fa" => true, // Persian / Farsi
            "ur" => true, // Urdu
            _ => false
        };
    }

    /// <summary>Applies RTL layout to a form and all its children.</summary>
    public static void ApplyRtl(Form form)
    {
        form.RightToLeft = RightToLeft.Yes;
        form.RightToLeftLayout = true;
        ApplyRtlRecursive(form);
    }

    private static void ApplyRtlRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is TextBox || c is RichTextBox || c is ComboBox || c is DataGridView ||
                c is TreeView || c is ListView || c is ListBox || c is CheckedListBox)
            {
                c.RightToLeft = RightToLeft.Yes;
            }
            if (c.Controls.Count > 0)
                ApplyRtlRecursive(c);
        }
    }
}
