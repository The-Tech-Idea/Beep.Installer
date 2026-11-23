using System.Text;
using System.Security.AccessControl;

namespace Beep.Installer.Engine;

/// <summary>Flushes a same-directory temporary file before publishing one complete checkpoint.</summary>
internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents)
        => WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
        => Write(path, stream => stream.Write(contents));

    public static void CopyFile(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        FileSecurity? copiedAccess = OperatingSystem.IsWindows() ? input.GetAccessControl() : null;
        var copiedWriteTime = File.GetLastWriteTimeUtc(source);
        Write(destination, output => input.CopyTo(output), copiedAccess, copiedWriteTime);
    }

    public static void ValidatePath(string path)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Checkpoint paths cannot traverse filesystem links.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Checkpoint target cannot be a filesystem link.");
    }

    private static FileStream CreateTemporary(string path, bool privateContents)
    {
        if (OperatingSystem.IsWindows() && privateContents)
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, security);
        }
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    private static void Write(string path, Action<Stream> write, FileSecurity? copiedAccess = null, DateTime? copiedWriteTime = null)
    {
        path = Path.GetFullPath(path);
        ValidatePath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = CreateTemporary(temporary, privateContents: copiedAccess is not null))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            if (copiedWriteTime.HasValue) File.SetLastWriteTimeUtc(temporary, copiedWriteTime.Value);
            if (OperatingSystem.IsWindows() && (copiedAccess is not null || File.Exists(path)))
            {
                var access = new FileSecurity();
                access.SetSecurityDescriptorSddlForm((copiedAccess ?? new FileInfo(path).GetAccessControl())
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                if (copiedAccess is not null)
                    access.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
                new FileInfo(temporary).SetAccessControl(access);
            }
            // Publish by one same-directory replacement rename. Windows ReplaceFile can leave
            // the destination renamed away when the process is killed during its metadata merge.
            // Existing checkpoint access controls are retained on the replacement.
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Diag.Debug("AtomicFileWriter", "Temporary checkpoint cleanup failed.", ex); }
        }
    }
}
