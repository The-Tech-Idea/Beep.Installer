using System.Drawing;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class ThemeLoaderTests
{
    [Fact]
    public void ParseColor_AcceptsHashPrefix()
    {
        var c = ThemeLoader.ParseColor("#FF8800", Color.Black);
        c.R.Should().Be(0xFF);
        c.G.Should().Be(0x88);
        c.B.Should().Be(0x00);
    }

    [Fact]
    public void ParseColor_AcceptsNoHashPrefix()
    {
        var c = ThemeLoader.ParseColor("2962FF", Color.Black);
        c.R.Should().Be(0x29);
        c.G.Should().Be(0x62);
        c.B.Should().Be(0xFF);
    }

    [Fact]
    public void ParseColor_ReturnsFallback_OnEmpty()
    {
        ThemeLoader.ParseColor("", Color.Red).Should().Be(Color.Red);
        ThemeLoader.ParseColor(null, Color.Blue).Should().Be(Color.Blue);
    }

    [Fact]
    public void ParseColor_ReturnsFallback_OnInvalid()
    {
        ThemeLoader.ParseColor("not-a-color", Color.Pink).Should().Be(Color.Pink);
        ThemeLoader.ParseColor("#ZZZZZZ", Color.Green).Should().Be(Color.Green);
    }
}
