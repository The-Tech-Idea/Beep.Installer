using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public enum ComparisonProjectReadError { None, Unavailable, TooLarge, Invalid }
public sealed record ComparisonProjectReadResult(InstallProject? Project, ComparisonProjectReadError Error);

public static class ComparisonProjectReader
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public static ComparisonProjectReadResult Read(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) return new(null, ComparisonProjectReadError.TooLarge);
            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd(); cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(script)) return new(null, ComparisonProjectReadError.Invalid);
            // Parser errors can quote project values. Return a typed error, never their raw text.
            var parsed = InstallerScriptSerializer.Read(script, Path.GetFileNameWithoutExtension(path));
            if (parsed.project is not null)
            {
                InstallerScriptSerializer.ResolveRelativePaths(parsed.project, path);
                parsed.project.MarkClean();
            }
            return parsed.project is null ? new(null, ComparisonProjectReadError.Invalid) : new(parsed.project, ComparisonProjectReadError.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(null, ComparisonProjectReadError.Unavailable); }
    }
}
