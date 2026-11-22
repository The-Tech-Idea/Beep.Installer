using System.Windows.Forms;

namespace Beep.Installer.Ui;

internal static class AuthoringHistoryKeyboard
{
    internal static bool UndoFocusedText(Control root)
    {
        if (root is TextBoxBase { Focused: true, ReadOnly: false, CanUndo: true } text)
        {
            text.Undo();
            return true;
        }
        foreach (Control child in root.Controls)
            if (child.ContainsFocus && UndoFocusedText(child)) return true;
        return false;
    }
}
