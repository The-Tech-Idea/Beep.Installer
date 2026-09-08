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
    public static void ApplyRtl(Form form) => ApplyDirection(form, rightToLeft: true);

    /// <summary>
    /// Sets reading direction on a form and its children, in both directions.
    ///
    /// This only ever turned RTL on. That is enough when the language is chosen once at startup,
    /// but a live switch from Arabic back to English left the window mirrored, because nothing
    /// could put it back.
    /// </summary>
    public static void ApplyDirection(Form form, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(form);

        var direction = rightToLeft ? RightToLeft.Yes : RightToLeft.No;
        form.RightToLeft = direction;
        form.RightToLeftLayout = rightToLeft;
        ApplyDirectionRecursive(form, direction);
    }

    private static void ApplyDirectionRecursive(Control parent, RightToLeft direction)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is TextBox || c is RichTextBox || c is ComboBox || c is DataGridView ||
                c is TreeView || c is ListView || c is ListBox || c is CheckedListBox)
            {
                c.RightToLeft = direction;
            }
            if (c.Controls.Count > 0)
                ApplyDirectionRecursive(c, direction);
        }
    }
}
