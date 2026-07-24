using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TheTechIdea.Beep.Updates;

namespace Beep.Installer.Extensions.Winform
{
    /// <summary>
    /// The one-call entry point a WinForms app uses to offer self-update. Wraps a BeepDM
    /// <see cref="IAppUpdateService"/> with the <see cref="UpdateDialog"/> UI:
    ///
    /// <code>
    /// // on a menu item, or after the main form loads:
    /// await BeepUpdater.CheckAndPromptAsync(this, updateService);
    /// </code>
    ///
    /// or, without wiring DI, straight from settings:
    /// <code>
    /// await BeepUpdater.CheckAndPromptAsync(this, new UpdateSettings { FeedUrl = "https://…/feed.json", CurrentVersion = "1.2.0" });
    /// </code>
    /// </summary>
    public static class BeepUpdater
    {
        /// <summary>
        /// Checks the feed and, when an app or module update is available, shows the update dialog.
        /// Returns true if the user applied an update. When <paramref name="showWhenUpToDate"/> is
        /// true, a simple "you're up to date" message is shown instead of doing nothing.
        /// </summary>
        public static async Task<bool> CheckAndPromptAsync(
            IWin32Window? owner, IAppUpdateService service, bool showWhenUpToDate = false, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(service);

            var check = await service.CheckAsync(ct).ConfigureAwait(true);

            if (!check.Succeeded)
            {
                if (showWhenUpToDate)
                    MessageBox.Show(owner as IWin32Window, $"Could not check for updates:{Environment.NewLine}{check.Error}",
                        "Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!check.AnyUpdateAvailable)
            {
                if (showWhenUpToDate)
                    MessageBox.Show(owner as IWin32Window, "The application is up to date.",
                        "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return UpdateDialog.ShowFor(owner, check, service);
        }

        /// <summary>Convenience overload that builds the service from settings (no DI required).</summary>
        public static Task<bool> CheckAndPromptAsync(
            IWin32Window? owner, UpdateSettings settings, bool showWhenUpToDate = false, CancellationToken ct = default)
            => CheckAndPromptAsync(owner, new AppUpdateService(settings), showWhenUpToDate, ct);

        /// <summary>
        /// A quiet check for use just after the main window opens: prompts only when an update is
        /// available, and never surfaces an error or a "nothing to do" message. Failures are logged
        /// to the debugger, not shown — a startup update check must never interrupt a launch.
        /// </summary>
        public static async Task CheckOnStartupAsync(IWin32Window? owner, IAppUpdateService service, CancellationToken ct = default)
        {
            try { await CheckAndPromptAsync(owner, service, showWhenUpToDate: false, ct).ConfigureAwait(true); }
            catch (Exception ex) { Debug.WriteLine($"[BeepUpdater] startup check skipped: {ex.Message}"); }
        }
    }
}
