using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Beep.Installer.Engine;

/// <summary>
/// Embeds an .ico resource into a PE (.exe/.dll) using the Win32 <c>UpdateResource</c> API.
/// </summary>
public static class PeIconEmbedder
{
    private static readonly IntPtr RT_ICON = (IntPtr)3;
    private static readonly IntPtr RT_GROUP_ICON = (IntPtr)14;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResourceW(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    /// <summary>
    /// Tries to embed an .ico into a PE file. Returns (success, error message).
    /// </summary>
    public static (bool ok, string? error) EmbedIcon(string pePath, string icoPath)
    {
        if (!File.Exists(pePath)) return (false, $"PE file not found: {pePath}");
        if (!File.Exists(icoPath)) return (false, $"Icon file not found: {icoPath}");

        try
        {
            var icoBytes = File.ReadAllBytes(icoPath);
            if (icoBytes.Length < 22)
                return (false, "Invalid .ico file (too short).");

            // ICO header: 6 bytes header, then 16-byte directory entries
            // Parse the number of images
            var count = BitConverter.ToUInt16(icoBytes, 4);
            if (count == 0) return (false, ".ico file contains no images.");

            var hUpdate = BeginUpdateResourceW(pePath, false);
            if (hUpdate == IntPtr.Zero)
                return (false, $"BeginUpdateResource failed: {Marshal.GetLastWin32Error()}");

            try
            {
                const ushort langId = 1033; // MAKELANGID(LANG_ENGLISH, SUBLANG_DEFAULT)

                for (ushort i = 0; i < count; i++)
                {
                    var dirEntryOffset = 6 + i * 16;
                    var imageOffset = BitConverter.ToInt32(icoBytes, dirEntryOffset + 12);
                    var imageSize = BitConverter.ToInt32(icoBytes, dirEntryOffset + 8);
                    var imageData = new byte[imageSize];
                    Array.Copy(icoBytes, imageOffset, imageData, 0, imageSize);

                    var resId = (IntPtr)(101 + i);
                    if (!UpdateResourceW(hUpdate, RT_ICON, resId, langId, imageData, imageData.Length))
                        return (false, $"UpdateResource (icon image {i}) failed: {Marshal.GetLastWin32Error()}");
                }

                var groupData = BuildGroupIconResource(icoBytes);
                if (!UpdateResourceW(hUpdate, RT_GROUP_ICON, (IntPtr)1, langId, groupData, groupData.Length))
                    return (false, $"UpdateResource (group icon) failed: {Marshal.GetLastWin32Error()}");

                if (!EndUpdateResourceW(hUpdate, false))
                    return (false, $"EndUpdateResource failed: {Marshal.GetLastWin32Error()}");

                return (true, null);
            }
            catch
            {
                EndUpdateResourceW(hUpdate, true); // discard
                throw;
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Builds a GRPICONDIRENTRY structure referencing the given icon resource ID.
    /// </summary>
    private static byte[] BuildGroupIconResource(byte[] icoFile)
    {
        var count = BitConverter.ToUInt16(icoFile, 4);
        var ms = new MemoryStream();
        ms.Write(icoFile, 0, 6); // Copy the first 6 header bytes

        for (int i = 0; i < count; i++)
        {
            var dirEntryOffset = 6 + i * 16;
            ms.Write(icoFile, dirEntryOffset, 12);
            var id = (ushort)(101 + i);
            var idBytes = BitConverter.GetBytes(id);
            ms.Write(idBytes, 0, 2);
        }
        return ms.ToArray();
    }
}
