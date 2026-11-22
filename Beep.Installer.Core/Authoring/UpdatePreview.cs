using Beep.Installer.Engine.Updates;

namespace Beep.Installer.Engine;

public enum UpdatePreviewPayload { Unavailable, LocalDelta, RemoteDelta }
public enum UpdatePreviewBlocker { None, Selection, MissingDeltaKey, MissingSource, InvalidLocalSignature, LocalContractMismatch, LocalUnavailable, AcquisitionPolicyDenied }
public sealed record UpdatePreviewSnapshot(string FeedSha256, string CurrentChannel, string TargetChannel,
    string InstalledVersion, string FeedVersion, string TargetVersion, string RollbackVersion, string Action,
    bool FeedTrusted, bool TransitionAllowed, UpdateDeltaSelectionStatus Selection,
    UpdatePreviewPayload Payload, UpdatePreviewBlocker Blocker, long DeltaBytes, long TargetBytes,
    string Source, bool LocalSignatureTrusted)
{
    public bool CanApply => FeedTrusted && TransitionAllowed && FeedSha256.Length == 64 && FeedSha256.All(Uri.IsHexDigit)
        && Selection == UpdateDeltaSelectionStatus.Selected && Blocker == UpdatePreviewBlocker.None;
    public bool FullPackageFallbackAvailable => false;
}

public static class UpdatePreview
{
    /// <summary>Reads signed local metadata if supplied; does not stage, apply, rotate backups or edit journals.</summary>
    public static UpdatePreviewSnapshot Create(UpdateChannelCheckResult check, string installedVersion,
        string localDeltaDirectory, IReadOnlyList<string> trustedDeltaKeys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = UpdateChannelFeedPackageService.SelectDelta(check, installedVersion);
        var package = selection.Package;
        var local = !string.IsNullOrWhiteSpace(localDeltaDirectory);
        var payload = package is null ? UpdatePreviewPayload.Unavailable
            : local ? UpdatePreviewPayload.LocalDelta : string.IsNullOrWhiteSpace(package.PackageBaseUrl) ? UpdatePreviewPayload.Unavailable : UpdatePreviewPayload.RemoteDelta;
        var blocker = package is null ? UpdatePreviewBlocker.Selection
            : !HasUsableKey(trustedDeltaKeys) ? UpdatePreviewBlocker.MissingDeltaKey
            : payload == UpdatePreviewPayload.Unavailable ? UpdatePreviewBlocker.MissingSource : UpdatePreviewBlocker.None;
        var localTrusted = false;
        if (package is not null && payload == UpdatePreviewPayload.RemoteDelta && blocker == UpdatePreviewBlocker.None
            && Beep.Installer.Policy.InstallerPolicyEvaluator.EvaluateUpdateChannel(check.PolicyEvaluation?.Policy ?? new Beep.Installer.Policy.InstallerPolicy(),
                check.Decision!.TargetChannelId, package.PackageBaseUrl).HasErrors)
            blocker = UpdatePreviewBlocker.AcquisitionPolicyDenied;
        if (package is not null && local && blocker == UpdatePreviewBlocker.None)
        {
            try
            {
                var verified = new DeltaUpdatePackageService().Verify(localDeltaDirectory, true, trustedDeltaKeys);
                localTrusted = verified.Trusted;
                if (!verified.Trusted || verified.Manifest is null) blocker = UpdatePreviewBlocker.InvalidLocalSignature;
                else if (!verified.Manifest.BaseVersion.Equals(package.BaseVersion, StringComparison.OrdinalIgnoreCase)
                    || !verified.Manifest.TargetVersion.Equals(package.TargetVersion, StringComparison.OrdinalIgnoreCase)
                    || !verified.Manifest.BaseTreeSha256.Equals(package.BaseTreeSha256, StringComparison.OrdinalIgnoreCase)
                    || !verified.Manifest.TargetTreeSha256.Equals(package.TargetTreeSha256, StringComparison.OrdinalIgnoreCase))
                    blocker = UpdatePreviewBlocker.LocalContractMismatch;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
            { blocker = UpdatePreviewBlocker.LocalUnavailable; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(check.Verification.FeedSha256, check.Decision?.CurrentChannelId ?? "", check.Decision?.TargetChannelId ?? "",
            installedVersion, check.Verification.Manifest?.AppVersion ?? "", package?.TargetVersion ?? "", check.Decision?.RollbackVersion ?? "",
            check.Decision?.Action ?? UpdateChannelTransitionEvaluator.ActionBlocked, check.Verification.SignatureTrusted,
            check.Decision?.Allowed == true && check.Success, selection.Status, payload, blocker,
            package?.DeltaBytes ?? 0, package?.TargetBytes ?? 0, payload == UpdatePreviewPayload.LocalDelta ? localDeltaDirectory : package?.PackageBaseUrl ?? "", localTrusted);
    }

    private static bool HasUsableKey(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { using var rsa = System.Security.Cryptography.RSA.Create(); rsa.ImportFromPem(key); return true; }
            catch (Exception ex) when (ex is ArgumentException or System.Security.Cryptography.CryptographicException) { }
        }
        return false;
    }
}
