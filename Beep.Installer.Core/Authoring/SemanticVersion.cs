using System;
using System.Runtime.CompilerServices;

// Core ships as the TheTechIdea.Beep.Installer.Sdk package, so internals stay internal rather than
// widening that public surface. The shell and the test project are the same product, not consumers
// of the SDK, so they are named as friends instead.
[assembly: InternalsVisibleTo("Beep.Installer")]
[assembly: InternalsVisibleTo("Beep.Installer.Tests")]

namespace Beep.Installer.Engine;

/// <summary>
/// Semantic-version parsing for authoring validation, delegating to BeepDM's
/// <see cref="TheTechIdea.Beep.ConfigUtil.SemVer"/> so the installer and the engine agree on what
/// counts as a version.
///
/// There were two private copies of this in Core and they disagreed with each other: the schema
/// service stripped a <c>-prerelease</c> suffix but not <c>+build</c>, and padded a bare
/// <c>"1"</c> to <c>"1.0"</c>; the extension validator stripped both suffixes but rejected a bare
/// <c>"1"</c> outright. So <c>1</c> was a valid version in one validator and invalid in the other,
/// and <c>1.0+build</c> was the reverse — for the same authored project.
/// </summary>
internal static class SemanticVersion
{
    /// <summary>
    /// Parses Major[.Minor[.Patch]] with an optional <c>-prerelease</c> or <c>+build</c> suffix.
    /// Ordering is by Major.Minor.Patch, matching the engine: prerelease metadata is parsed off
    /// and ignored rather than being made to sort.
    /// </summary>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!TheTechIdea.Beep.ConfigUtil.SemVer.TryParse(value ?? "", out var major, out var minor, out var patch))
            return false;

        version = new Version(major, minor, patch);
        return true;
    }
}
