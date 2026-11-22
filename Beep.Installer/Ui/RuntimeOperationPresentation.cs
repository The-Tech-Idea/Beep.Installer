using Beep.Installer.Engine;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Ui;

internal static class RuntimeOperationPresentation
{
    public static string Describe(RuntimeOperationOutcome outcome)
    {
        var lines = new List<string>
        {
            outcome.RestartRequired
                ? L("Runtime_RestartRequired", "Restart Windows to finish applying the installation changes.")
                : L("Runtime_NoRestartReported", "The installer did not report a required restart.")
        };
        if (!outcome.Succeeded)
        {
            // RollbackManager catches some errors within individual actions. Equal counts do
            // not establish that files or system resources were fully restored.
            lines.Add(outcome.RollbackAttempted
                ? string.Format(L("Runtime_RollbackAttempted", "Recovery was attempted: {0} of {1} rollback actions returned without an error. Full recovery is not verified; review the log before retrying."),
                    outcome.RollbackActionsCompleted, outcome.RollbackActionsRequested)
                : L("Runtime_RecoveryUnknown", "Recovery has not been verified. Review the log and installation state before retrying."));
            lines.Add(outcome.BackupRecovery switch
            {
                BackupRecoveryState.Restored => L("Runtime_BackupRestored", "The previous-version file backup was restored. This does not verify recovery of system resources."),
                BackupRecoveryState.Failed => L("Runtime_BackupFailed", "Restoring the previous-version file backup failed. Changes may remain."),
                BackupRecoveryState.Unknown => L("Runtime_BackupUnknown", "The previous-version backup recovery outcome is unknown."),
                _ => L("Runtime_BackupNotAttempted", "No previous-version file backup restore was attempted.")
            });
        }
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
