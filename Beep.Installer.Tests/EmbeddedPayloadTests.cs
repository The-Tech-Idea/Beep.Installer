using Beep.Installer.Models;
using System.IO;
using System.IO.Compression;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 1 (Track A2.3) — self-extracting payload embedding.</summary>
public class EmbeddedPayloadTests
{
    [Fact]
    public void EmbedThenFindThenExtract_RoundTrips()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepemb_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            // Create a minimal InstallerOutputFormat.Exe (any bytes) and a payload zip.
            var exe = Path.Combine(tmp, "Setup.exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A, 0x90, 0x00 }); // fake MZ header
            var zip = Path.Combine(tmp, "payload.zip");
            // Write a tiny valid zip containing "hello.txt".
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("hello.txt");
                using var s = e.Open();
                s.Write(new byte[] { 72, 101, 108, 108, 111 }, 0, 5);
            }

            PePayloadWriter.Embed(exe, zip);
            // After embedding, the EXE is larger and the payload zip is untouched (caller deletes it).
            new FileInfo(exe).Length.Should().BeGreaterThan(4 + 5 + 30, "exe grew by zip + footer");

            var found = PePayloadWriter.FindEmbedded(exe);
            found.Should().NotBeNull();
            found!.Value.length.Should().BeGreaterThan(10);

            // Extract to a new temp zip.
            var outZip = Path.Combine(tmp, "out.zip");
            PePayloadWriter.Extract(exe, outZip);
            File.Exists(outZip).Should().BeTrue();

            using var outZ = ZipFile.OpenRead(outZip);
            var entry = outZ.GetEntry("hello.txt");
            entry.Should().NotBeNull();
            using var sr = new StreamReader(entry!.Open());
            sr.ReadToEnd().Should().Be("Hello");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void FindEmbedded_ReturnsNull_ForPlainExe()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepemb2_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var exe = Path.Combine(tmp, "plain.exe");
            File.WriteAllBytes(exe, new byte[100]); // no footer
            PePayloadWriter.FindEmbedded(exe).Should().BeNull();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }
}
