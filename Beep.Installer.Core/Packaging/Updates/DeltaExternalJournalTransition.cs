using System.Text;
using Beep.Installer.Extensibility;

namespace Beep.Installer.Engine.Updates;

/// <summary>Machine-local before/after checkpoint snapshots for the existing delta transaction.</summary>
public sealed record DeltaExternalJournalTransition
{
    public string JournalPath { get; init; } = "";
    public string BaseContent { get; init; } = "";
    public string TargetContent { get; init; } = "";

    internal static DeltaExternalJournalTransition Create(string root, DeltaInstalledImage image,
        ResourceExecutionJournal source, ResourceExecutionJournal target)
    {
        var path = ResourceExecutionJournalStore.ResolvePath(root, image.AppId);
        var bytes = File.ReadAllBytes(path);
        var captured = ResourceExecutionJournalStore.Deserialize(Encoding.UTF8.GetString(bytes));
        if (ResourceExecutionJournalStore.Serialize(captured) != ResourceExecutionJournalStore.Serialize(source))
            throw new IOException("External journal changed while preparing its transaction snapshots.");
        return new()
        {
            JournalPath = path, BaseContent = Convert.ToBase64String(bytes),
            TargetContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(ResourceExecutionJournalStore.Serialize(target)))
        };
    }

    internal ResourceExecutionJournal TargetJournal => ResourceExecutionJournalStore.Deserialize(
        Encoding.UTF8.GetString(Convert.FromBase64String(TargetContent)));

    internal IDisposable Acquire(string root, DeltaInstalledImage image, string? locationRoot = null)
    {
        ValidateLocation(root, image, locationRoot);
        return InstallationOperationLock.Acquire(JournalPath);
    }

    internal void Reconcile(string root, DeltaInstalledImage image, bool target, bool dryRun = false, bool requireBase = false, string? locationRoot = null)
    {
        ValidateLocation(root, image, locationRoot);
        var before = Convert.FromBase64String(BaseContent);
        var after = Convert.FromBase64String(TargetContent);
        var actual = File.ReadAllBytes(JournalPath);
        var isBase = actual.AsSpan().SequenceEqual(before);
        var isTarget = actual.AsSpan().SequenceEqual(after);
        if ((!isBase && !isTarget) || (requireBase && !isBase))
            throw new IOException("External resource journal differs from both transaction snapshots; recovery data was preserved.");
        if (!dryRun && !(target ? isTarget : isBase))
            AtomicFileWriter.WriteAllBytes(JournalPath, target ? after : before);
    }

    private void ValidateLocation(string root, DeltaInstalledImage image, string? locationRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), JournalPath);
        if (!image.ExternalJournal || !Path.IsPathFullyQualified(JournalPath)
            || !string.Equals(ResourceExecutionJournalStore.ResolvePath(locationRoot ?? root, image.AppId, journalOwnerRoot: root), JournalPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !(Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new IOException("External journal transaction does not match the installation's recorded location.");
    }
}
