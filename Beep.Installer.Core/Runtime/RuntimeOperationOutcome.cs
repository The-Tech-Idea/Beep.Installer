namespace Beep.Installer.Engine;

public enum BackupRecoveryState { NotAttempted, Restored, Failed, Unknown }

/// <summary>Detached evidence from the existing runtime; it never executes recovery.</summary>
public sealed record RuntimeOperationOutcome(
    bool Succeeded,
    bool RestartRequired,
    bool RollbackAttempted = false,
    int RollbackActionsRequested = 0,
    int RollbackActionsCompleted = 0,
    BackupRecoveryState BackupRecovery = BackupRecoveryState.NotAttempted);
