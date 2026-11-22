using System;
using Beep.Installer.Engine;
using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

internal static class SaveStatePresentation
{
    internal static string Caption(AuthoringSaveState state)
    {
        if (state.SaveError is not null) return L("SaveState_Failed", "Save failed");
        if (state.AutoSaveError is not null) return L("SaveState_AutoSaveFailed", "Recovery draft failed");
        if (state.RecoveryError is not null) return L("SaveState_RecoveryFailed", "Recovery draft could not be loaded");
        if (state.CleanupError is not null) return L("Recovery_CleanupFailed", "Saved; recovery draft cleanup failed");
        if (state.IsRecovered) return L("SaveState_Recovered", "Recovered draft — save to keep changes");
        if (state.IsDirty) return L("SaveState_Unsaved", "Unsaved changes");
        return state.DocumentPath is null ? L("SaveState_Untitled", "Not saved yet") : L("SaveState_Saved", "Saved");
    }

    internal static string Details(AuthoringSaveState state)
        => $"{Caption(state)}{Environment.NewLine}{state.DocumentPath ?? L("SaveState_Untitled", "Not saved yet")}" +
           $"{Environment.NewLine}{L("SaveState_RecoveryPath", "Recovery draft")}: {state.RecoveryPath}" +
           (state.LastSavedAt is DateTime saved ? $"{Environment.NewLine}{L("SaveState_Saved", "Saved")}: {saved.ToLocalTime():g}" : "") +
           (state.LastAutoSavedAt is DateTime autoSaved ? $"{Environment.NewLine}{L("SaveState_RecoveryPath", "Recovery draft")}: {autoSaved.ToLocalTime():g}" : "") +
           (state.SaveError is not null ? Environment.NewLine + state.SaveError : "") +
           (state.AutoSaveError is not null ? Environment.NewLine + state.AutoSaveError : "") +
           (state.RecoveryError is not null ? Environment.NewLine + state.RecoveryError : "") +
           (state.CleanupError is not null ? Environment.NewLine + state.CleanupError : "");

    private static string L(string key, string fallback) => LanguageManager.GetOrDefault(key, fallback);
}
