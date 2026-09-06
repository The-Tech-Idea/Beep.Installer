using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beep.Installer.Engine;

/// <summary>
/// Nonblocking cross-process lease for synchronous installation-directory operations.
/// Acquire and dispose on the same thread, covering all mutations including failure rollback.
/// </summary>
public sealed class InstallationOperationLock : IDisposable
{
    [ThreadStatic] private static HashSet<string>? _heldByThread;
    [ThreadStatic] private static HashSet<string>? _runningGraphs;
    private readonly Mutex _mutex;
    private readonly string _name;

    private InstallationOperationLock(Mutex mutex, string name) { _mutex = mutex; _name = name; }

    public static InstallationOperationLock Acquire(string installDirectory)
    {
        var name = LockName(installDirectory);
        _heldByThread ??= new HashSet<string>(StringComparer.Ordinal);
        if (_heldByThread.Contains(name)) throw new IOException("Another operation is already active for this installation.");
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another operation is already active for this installation.");
            _heldByThread.Add(name);
            return new InstallationOperationLock(mutex, name);
        }
        catch { mutex.Dispose(); throw; }
    }

    internal static T RunGraph<T>(string installDirectory, Func<T> run)
    {
        var name = LockName(installDirectory);
        _runningGraphs ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_runningGraphs.Add(name)) throw new IOException("An installer graph is already executing for this installation.");
        try
        {
            // Host transactions retain their outer lease through commit and failure rollback.
            using var lease = _heldByThread?.Contains(name) == true ? null : Acquire(installDirectory);
            return run();
        }
        finally { _runningGraphs.Remove(name); }
    }

    private static string LockName(string installDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        if (OperatingSystem.IsWindows()) path = ResolveWindowsPath(path).ToUpperInvariant();
        return (OperatingSystem.IsWindows() ? "Global\\" : "") + "BeepInstaller-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    }

    private static string ResolveWindowsPath(string path)
    {
        var missing = new Stack<string>();
        var ancestor = path;
        while (true)
        {
            using var handle = CreateFileW(ancestor, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                var buffer = new StringBuilder(512);
                // NT device names collapse drive mappings, short names and junction aliases.
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 2);
                if (length >= buffer.Capacity)
                {
                    buffer.EnsureCapacity(checked((int)length + 1));
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 2);
                }
                if (length == 0 || length >= buffer.Capacity)
                    throw new IOException("Cannot resolve the installation coordination path.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                var resolved = buffer.ToString().TrimEnd('\\');
                return missing.Count == 0 ? resolved : resolved + "\\" + string.Join("\\", missing);
            }
            var error = Marshal.GetLastWin32Error();
            if (error is not (2 or 3))
                throw new IOException("Cannot open the installation coordination path.", new System.ComponentModel.Win32Exception(error));
            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || parent == ancestor)
                throw new IOException("Installation coordination requires a reachable filesystem root.");
            missing.Push(Path.GetFileName(ancestor));
            ancestor = parent;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    public void Dispose()
    {
        _heldByThread?.Remove(_name);
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
