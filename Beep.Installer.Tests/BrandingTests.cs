using System.Drawing;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Covers the branding colour parsing the wizard relies on.
///
/// <see cref="ThemeLoader"/> and <see cref="BannerLoader"/> existed but had **no callers**:
/// the builder collected sidebar colours, an accent colour, a banner and an icon, wrote them
/// into the .bsetup, and the wizard then hardcoded its own palette. Authors were configuring
/// branding that silently did nothing.
/// </summary>
public class BrandingTests
{
    private static readonly Color Fallback = Color.FromArgb(1, 2, 3);

    [Theory]
    [InlineData("#101820", 0x10, 0x18, 0x20)]
    [InlineData("101820", 0x10, 0x18, 0x20)]
    [InlineData("  #2962FF  ", 0x29, 0x62, 0xFF)]
    [InlineData("#ffffff", 0xFF, 0xFF, 0xFF)]
    public void ParsesHexColours(string input, int r, int g, int b)
    {
        var color = ThemeLoader.ParseColor(input, Fallback);

        color.R.Should().Be((byte)r);
        color.G.Should().Be((byte)g);
        color.B.Should().Be((byte)b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("#12")]          // too short
    [InlineData("#1234567")]     // too long
    [InlineData("#GGGGGG")]      // not hex
    public void FallsBackOnUnusableInput(string? input)
    {
        // A malformed colour in a .bsetup must not break the wizard.
        ThemeLoader.ParseColor(input!, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void BannerLoader_ReturnsNull_ForMissingFile()
    {
        // Branding assets are optional; a missing banner must degrade quietly.
        BannerLoader.Load(@"C:\does\not\exist\banner.png").Should().BeNull();
        BannerLoader.Load(null).Should().BeNull();
        BannerLoader.Load("").Should().BeNull();
    }
}
