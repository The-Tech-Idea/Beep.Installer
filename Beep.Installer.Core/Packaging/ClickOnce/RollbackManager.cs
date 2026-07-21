using System;
using System.IO;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Outcome of a rollback rotation or a rollback swap (Track B3.3).</summary>
public class RollbackResult
{
    public bool Success { get; set; }
    public string CurrentInstallRoot { get; set; } = "";
    public int BackupCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Keeps the last <c>keep</c> prior versions of an install as <c>&lt;installRoot&gt;.bak1</c>,
/// <c>.bak2</c>, ... <c>.bakN</c>. After <see cref="Rotate"/> the current install is the
/// freshly-applied one; <see cref="Rollback"/> swaps <c>.bak1</c> back to current and demotes
/// the previous current into <c>.bak1</c>. Pure filesystem renames so it is unit-testable.
/// </summary>
public static class RollbackManager
{
    /// <summary>Default number of backup versions to retain.</summary>
    public const int DefaultKeep = 3;

    private static string Bak(int i, string installRoot) => installRoot + ".bak" + i;
    private static string SwapDir(string installRoot) => installRoot + ".swap";

    /// <summary>
    /// Promotes <paramref name="stageRoot"/> into <paramref name="installRoot"/> and rotates the
    /// previous <paramref name="installRoot"/> into the backup chain, keeping the last
    /// <paramref name="keep"/> prior versions.
    /// </summary>
    public static RollbackResult Rotate(string installRoot, string stageRoot, int keep = DefaultKeep)
    {
        var r = new RollbackResult { CurrentInstallRoot = installRoot };
        if (keep < 1) { r.Error = "keep must be >= 1"; return r; }
        try
        {
            if (!Directory.Exists(stageRoot)) { r.Error = "Stage directory missing."; return r; }

            // 1) Discard the oldest backup beyond the keep window.
            if (Directory.Exists(Bak(keep, installRoot)))
                Directory.Delete(Bak(keep, installRoot), recursive: true);

            // 2) Shift .bak{i} → .bak{i+1} for i = keep-1 .. 1 (oldest first to avoid clobbering).
            for (int i = keep - 1; i >= 1; i--)
                if (Directory.Exists(Bak(i, installRoot)))
                    Directory.Move(Bak(i, installRoot), Bak(i + 1, installRoot));

            // 3) Current install → .bak1.
            if (Directory.Exists(installRoot))
                Directory.Move(installRoot, Bak(1, installRoot));

            // 4) Stage → install.
            Directory.Move(stageRoot, installRoot);

            r.BackupCount = CountBackups(installRoot, keep);
            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            Diag.Warn("RollbackManager", "rotate failed", ex);
        }
        return r;
    }

    /// <summary>
    /// Swaps <c>.bak1</c> back into <paramref name="installRoot"/> and demotes the current
    /// install into <c>.bak1</c> (3-way swap via a hidden <c>.swap</c> temp dir).
    /// </summary>
    public static RollbackResult Rollback(string installRoot, int keep = DefaultKeep)
    {
        var r = new RollbackResult { CurrentInstallRoot = installRoot };
        try
        {
            if (!Directory.Exists(Bak(1, installRoot))) { r.Error = "No backup to roll back to."; return r; }

            var swap = SwapDir(installRoot);
            if (Directory.Exists(swap)) Directory.Delete(swap, recursive: true);

            Directory.Move(Bak(1, installRoot), swap);   // bak1 → .swap
            if (Directory.Exists(installRoot))
                Directory.Move(installRoot, Bak(1, installRoot)); // current → .bak1
            Directory.Move(swap, installRoot);          // .swap → current

            r.BackupCount = CountBackups(installRoot, keep);
            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            Diag.Warn("RollbackManager", "rollback failed", ex);
        }
        return r;
    }

    private static int CountBackups(string installRoot, int keep)
    {
        int n = 0;
        for (int i = 1; i <= keep; i++) if (Directory.Exists(Bak(i, installRoot))) n++;
        return n;
    }
}