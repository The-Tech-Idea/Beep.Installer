using System.IO;
using System.IO.Compression;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 1 (Track A2) — payload compression: solid/dedup + plain zip round-trip.</summary>
public class CompressionTests
{
    private readonly string _tempRoot;

    public CompressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepZip_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    private void Cleanup()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Solid_Deduplicates_IdenticalFiles()
    {
        var src = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(Path.Combine(src, "compA"));
        Directory.CreateDirectory(Path.Combine(src, "compB"));
        var dllBytes = new byte[4096];
        for (int i = 0; i < dllBytes.Length; i++) dllBytes[i] = (byte)(i % 251);
        File.WriteAllBytes(Path.Combine(src, "compA", "shared.dll"), dllBytes);
        File.WriteAllBytes(Path.Combine(src, "compB", "shared.dll"), dllBytes); // identical content
        File.WriteAllBytes(Path.Combine(src, "readme.txt"), new byte[] { 1, 2, 3 });

        var zipPath = Path.Combine(_tempRoot, "payload.zip");
        try
        {
            var (files, blobs, originalBytes, storedBytes) =
                PayloadPackager.CreateSolid(src, zipPath, CompressionLevel.Optimal);

            files.Should().Be(3);
            blobs.Should().Be(2, "the two identical shared.dll files collapse to one blob");
            storedBytes.Should().BeLessThan(originalBytes, "dedup must save the duplicate bytes");

            PayloadPackager.IsSolid(zipPath).Should().BeTrue();

            // Extract and verify all three files are materialized with correct content.
            var outDir = Path.Combine(_tempRoot, "out");
            PayloadPackager.ExtractSolid(zipPath, outDir);
            File.ReadAllBytes(Path.Combine(outDir, "compA", "shared.dll")).Should().Equal(dllBytes);
            File.ReadAllBytes(Path.Combine(outDir, "compB", "shared.dll")).Should().Equal(dllBytes);
            File.ReadAllBytes(Path.Combine(outDir, "readme.txt")).Should().Equal(new byte[] { 1, 2, 3 });
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void ExtractZip_HandlesPlainZip_WithBaseFolder()
    {
        // Builder zips a directory literally named "payload" with includeBaseDirectory:true.
        var payloadDir = Path.Combine(_tempRoot, "payload");
        Directory.CreateDirectory(payloadDir);
        File.WriteAllText(Path.Combine(payloadDir, "a.txt"), "a");

        var zipPath = Path.Combine(_tempRoot, "p.zip");
        ZipFile.CreateFromDirectory(payloadDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        PayloadPackager.IsSolid(zipPath).Should().BeFalse();

        try
        {
            var extractRoot = Path.Combine(_tempRoot, "out");
            var root = PayloadPackager.ExtractZip(zipPath, extractRoot, "payload");
            // Base folder "payload" present → root is extractRoot/payload
            root.Should().Be(Path.Combine(extractRoot, "payload"));
            File.Exists(Path.Combine(root, "a.txt")).Should().BeTrue();
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Builder_SolidCompression_ProducesSolidZip()
    {
        var tmp = Path.Combine(_tempRoot, "bld");
        Directory.CreateDirectory(tmp);
        try
        {
            var srcDir = Path.Combine(tmp, "src");
            Directory.CreateDirectory(Path.Combine(srcDir, "a"));
            Directory.CreateDirectory(Path.Combine(srcDir, "b"));
            File.WriteAllBytes(Path.Combine(srcDir, "a", "lib.dll"), new byte[] { 9, 9, 9 });
            File.WriteAllBytes(Path.Combine(srcDir, "b", "lib.dll"), new byte[] { 9, 9, 9 }); // dup

            var project = InstallerProjectFactory.CreateNew("Solid", "1.0.0", "P", srcDir);
            project.OutputDir = Path.Combine(tmp, "out");
            project.CompressPayload = true;
            project.SolidCompression = true;
            project.Compression = CompressionFormat.Zip;
            project.CreateUninstallEntry = false;
            project.UseTestDefaults();

            var result = TestHelpers.TestPipeline().Run(project);
            result.Success.Should().BeTrue();

            var zip = Path.Combine(project.OutputDir, "payload.zip");
            File.Exists(zip).Should().BeTrue();
            PayloadPackager.IsSolid(zip).Should().BeTrue();
            result.Warnings.Should().Contain(w => w.Contains("deduplicated"));
        }
        finally { Cleanup(); }
    }
}


