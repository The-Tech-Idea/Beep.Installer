using System;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

/// <summary>The interactive saved-project opening contract shared by startup and Advanced.</summary>
internal static class RecoveryProjectOpening
{
    internal static (bool ok, string? error, bool canceled) Open(InstallerController controller, string path,
        IWin32Window? owner = null, Func<DialogResult>? chooseRecovery = null)
    {
        var recover = false;
        if (AutoSave.IsRecoveryAvailable(path))
        {
            var choice = chooseRecovery?.Invoke() ?? MessageBox.Show(owner,
                LanguageManager.GetOrDefault("Recovery_Choice", "A newer recovery draft is available. Recover it? Choose No to open the saved project and retain the draft, or Cancel to keep the current project.") +
                Environment.NewLine + AutoSave.AutoSavePath(path),
                LanguageManager.GetOrDefault("Recovery_Title", "Recover unsaved project"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice is not (DialogResult.Yes or DialogResult.No)) return (false, null, true);
            recover = choice == DialogResult.Yes;
        }
        var result = controller.Open(path, recoverDraft: recover);
        return (result.ok, result.error, false);
    }
}
