using System;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// One version parser for authoring validation (2.C.1).
///
/// Core carried two private copies and they disagreed: the schema service stripped a
/// <c>-prerelease</c> suffix but not <c>+build</c>, and padded a bare <c>"1"</c> to <c>"1.0"</c>;
/// the extension validator stripped both suffixes but rejected a bare <c>"1"</c>. So the same
/// string was a valid version in one validator and invalid in the other. Both now delegate to
/// BeepDM's <c>SemVer</c>, which is also what the runtime version gate uses.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1", 1, 0, 0)]                 // the extension validator used to reject this
    [InlineData("2.5", 2, 5, 0)]
    [InlineData("3.4.5", 3, 4, 5)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]      // prerelease parsed off, not ordered
    [InlineData("1.0+build.77", 1, 0, 0)]      // the schema service used to reject this
    [InlineData("  4.1.2  ", 4, 1, 2)]
    public void ItParsesEveryFormTheEngineAccepts(string value, int major, int minor, int patch)
    {
        SemanticVersion.TryParse(value, out var version).Should().BeTrue();

        version.Should().Be(new Version(major, minor, patch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("x.y.z")]
    public void ItRefusesWhatIsNotAVersion(string? value)
    {
        SemanticVersion.TryParse(value, out var version).Should().BeFalse();

        version.Should().Be(new Version(0, 0, 0), "a refused parse still yields a usable zero");
    }

    [Fact]
    public void OrderingIgnoresPrereleaseMetadata_MatchingTheEngine()
    {
        // SemVer proper orders 1.2.3-beta before 1.2.3; the engine's gate compares
        // Major.Minor.Patch only, and the installer must agree with it rather than be cleverer.
        SemanticVersion.TryParse("1.2.3-beta.1", out var prerelease).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3", out var release).Should().BeTrue();

        prerelease.Should().Be(release);
    }
}
