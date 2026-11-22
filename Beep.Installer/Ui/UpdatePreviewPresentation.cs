using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

internal static class UpdatePreviewPresentation
{
    internal static string Describe(UpdatePreviewSnapshot preview)
    {
        var problem = preview.Blocker switch
        {
            UpdatePreviewBlocker.None => "Ready",
            UpdatePreviewBlocker.MissingDeltaKey => "MissingKey",
            UpdatePreviewBlocker.MissingSource => "MissingSource",
            UpdatePreviewBlocker.InvalidLocalSignature => "UntrustedDelta",
            UpdatePreviewBlocker.LocalContractMismatch => "Mismatch",
            UpdatePreviewBlocker.LocalUnavailable => "Unavailable",
            UpdatePreviewBlocker.AcquisitionPolicyDenied => "PolicyDenied",
            _ => preview.Selection switch
            {
                UpdateDeltaSelectionStatus.Missing => "MissingDelta",
                UpdateDeltaSelectionStatus.Ambiguous => "AmbiguousDelta",
                UpdateDeltaSelectionStatus.InvalidVersion => "InvalidVersion",
                _ => "NotAuthorized"
            }
        };
        return string.Join(Environment.NewLine, new[]
        {
            L("Channels", preview.CurrentChannel, preview.TargetChannel),
            L("Versions", preview.InstalledVersion, preview.TargetVersion, preview.FeedVersion),
            L(preview.Payload switch { UpdatePreviewPayload.LocalDelta => "Local", UpdatePreviewPayload.RemoteDelta => "Remote", _ => "NoPayload" }),
            preview.Source,
            L("Sizes", preview.DeltaBytes.ToString("N0"), preview.TargetBytes.ToString("N0")),
            L(problem), L("NoFallback"), L("Rollback", preview.RollbackVersion), L("Rechecks")
        });
    }

    private static string L(string key, params object[] args) => LanguageManager.Tf("UpdatePreview_" + key, args);
}
