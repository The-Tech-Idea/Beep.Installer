using System;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>
/// Publishes an authored project to a deployment location — the Track B counterpart to
/// <see cref="Build.IInstallerHostBuilder"/> on the build side.
///
/// The concrete publisher used to be a class called simply <c>Publisher</c>, which said nothing
/// about the fact that it emits ClickOnce application and deployment manifests specifically. With
/// MSIX and a plain feed publish also in the codebase, "the publisher" was ambiguous at every call
/// site and there was no seam to substitute one — including in tests, which had to do a real
/// ClickOnce publish to exercise anything that publishes.
/// </summary>
public interface IInstallerPublisher
{
    /// <summary>
    /// A short, stable identifier for the publish shape this implementation produces
    /// (<c>clickonce</c>, and room for others). Used in diagnostics and evidence, so it must not
    /// change once shipped.
    /// </summary>
    string Kind { get; }

    /// <summary>Progress reporting; ignored by implementations that have nothing to report.</summary>
    IProgress<(int percent, string message)>? Progress { get; set; }

    /// <summary>
    /// Publishes <paramref name="project"/> into <paramref name="publishDir"/>.
    /// </summary>
    /// <param name="updateUrl">
    /// Overrides the project's configured update URL when non-empty. This is the deployment
    /// provider URL baked into the manifest, so it has to be the address clients will actually
    /// check, not the folder written to now.
    /// </param>
    /// <param name="sign">
    /// Sign the manifests when a certificate is configured. An unsigned publish is a warning rather
    /// than a failure, so callers that require signing must inspect
    /// <see cref="PublishResult.Signed"/> — a successful result does not imply a signed one.
    /// </param>
    PublishResult Publish(InstallProject project, string publishDir, string? updateUrl = null, bool sign = true);
}
