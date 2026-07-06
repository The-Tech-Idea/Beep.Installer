using Beep.Installer.Models;
using System.IO;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase cross-cutting (X1) — diagnostics replace silent catches.</summary>
public class DiagTests
{
    [Fact]
    public void Warn_Is_Captured()
    {
        // The log file is a best-effort secondary sink; under parallel tests the file may be
        // briefly held by another writer. We assert the in-memory ring (the canonical diagnostic)
        // and tolerate the file check failing.
        Diag.Reset();
        var marker = "DiagTest_" + System.Guid.NewGuid().ToString("N");

        Diag.Warn("TestContext", marker);

        var entry = Diag.Recent.Should().ContainSingle(e => e.Message == marker).Subject;
        entry.Level.Should().Be("WARN");
        entry.Context.Should().Be("TestContext");

        try
        {
            File.Exists(Diag.LogPath).Should().BeTrue();
            File.ReadAllText(Diag.LogPath).Should().Contain(marker);
        }
        catch (IOException) { /* another test holds the log file — best-effort sink, not asserted */ }
    }

}
