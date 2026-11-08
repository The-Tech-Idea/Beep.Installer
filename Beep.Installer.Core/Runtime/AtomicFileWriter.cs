using System.Text;

namespace Beep.Installer.Engine;

/// <summary>Flushes a same-directory temporary file before publishing one complete checkpoint.</summary>
internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents)
        => WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Checkpoint paths cannot traverse filesystem links.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Checkpoint target cannot be a filesystem link.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporary, path, null);
                }
                catch (IOException)
                {
                    // ReplaceFile can fail after it has already renamed the destination (for example
                    // when an antivirus scanner or the uninstaller briefly holds the checkpoint).
                    // A same-volume overwrite move is the safe fallback and keeps the checkpoint usable.
                    File.Move(temporary, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
