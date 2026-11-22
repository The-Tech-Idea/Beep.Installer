namespace Beep.Installer.Engine;

/// <summary>The saved document and its recovery draft have separate identities.</summary>
public sealed record AuthoringSaveState(string? DocumentPath, string RecoveryPath, bool IsDirty,
    bool IsRecovered, System.DateTime? LastSavedAt, System.DateTime? LastAutoSavedAt,
    string? SaveError, string? AutoSaveError, string? RecoveryError, string? CleanupError = null);
