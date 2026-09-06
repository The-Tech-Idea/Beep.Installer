using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Beep.Installer.Engine;

/// <summary>
/// Self-extracting payload writer. Appends a compressed payload to the end of a PE (.exe)
/// followed by a fixed-size footer containing the payload offset and a magic marker.
/// The runtime reads its own EXE, seeks to the footer, and extracts the payload.
/// </summary>
public static class PePayloadWriter
{
    // 16-byte marker (kept short so signature scanning is cheap)
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("BEEPINSTPAYLOAD.");

    /// <summary>Appends <paramref name="payloadZipPath"/> to the end of <paramref name="exePath"/>.</summary>
    /// <remarks>
    /// The PE format allows appending arbitrary data after the last section as long as the
    /// PE header's "SizeOfImage" field is NOT consulted at load time (which is the case for
    /// Windows PE loader). However, .NET's own single-file bundle also lives at the end of
    /// the EXE. The caller MUST ensure the EXE was built with uncompressed single-file
    /// (no <c>EnableCompressionInSingleFile</c>), otherwise appending here corrupts the
    /// .NET host bundle and the resulting EXE will fail to load with a corrupted-assembly error.
    /// </remarks>
    public static void Embed(string exePath, string payloadZipPath)
    {
        var payload = File.ReadAllBytes(payloadZipPath);
        var exeBytes = File.ReadAllBytes(exePath);
        long offset = exeBytes.LongLength;
        using var fs = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(exeBytes, 0, exeBytes.Length);
        fs.Write(payload, 0, payload.Length);
        fs.Write(BitConverter.GetBytes(offset), 0, 8);
        fs.Write(Magic, 0, Magic.Length);
    }

    /// <summary>
    /// Scans the end of <paramref name="exePath"/> for the magic footer. Returns the byte
    /// offset and byte-length of the embedded payload, or null when no payload is embedded.
    /// </summary>
    public static (long offset, long length)? FindEmbedded(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        long fileLen = new FileInfo(exePath).Length;
        long footerLen = Magic.Length + 8;
        if (fileLen < footerLen) return null;

        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = fileLen - footerLen;
        var offBuf = new byte[8];
        var magBuf = new byte[Magic.Length];
        fs.ReadExactly(offBuf);
        fs.ReadExactly(magBuf);
        if (!magBuf.SequenceEqual(Magic)) return null;

        long offset = BitConverter.ToInt64(offBuf, 0);
        if (offset < 0 || offset >= fileLen - footerLen) return null;
        return (offset, fileLen - footerLen - offset);
    }

    /// <summary>Extracts the embedded payload to a standalone temp zip and returns its path (null when absent).</summary>
    public static string? Extract(string exePath, string tempZipPath)
    {
        var range = FindEmbedded(exePath);
        if (range == null) return null;
        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = range.Value.offset;
        var buf = new byte[range.Value.length];
        fs.ReadExactly(buf);
        File.WriteAllBytes(tempZipPath, buf);
        return tempZipPath;
    }
}
