using System;
using System.Drawing;
using System.Windows.Forms;

namespace Beep.Installer.Forms;

/// <summary>Lightweight single-field prompt used in place of Microsoft.VisualBasic.Interaction.InputBox.</summary>
public static class InputBox
{
    public static string Prompt(IWin32Window? owner, string prompt, string title, string defaultValue = "")
    {
        using var dlg = new Form
        {
            Text = title,
            Size = new Size(440, 160),
            StartPosition = FormStartPosition.CenterParent,
            // Absolute pixel sizes require DPI auto-scaling, or the dialog clips at 125%+.
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true, MaximumSize = new Size(400, 0) };
        var box = new TextBox { Location = new Point(12, 50), Size = new Size(400, 24), Text = defaultValue };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(252, 86), Size = new Size(75, 28) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(337, 86), Size = new Size(75, 28) };

        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Controls.AddRange(new Control[] { lbl, box, ok, cancel });

        return dlg.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
    }
}
