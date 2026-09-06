using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Source-level guards against the failure mode that produced most of this project's real
/// bugs: code that fails and says nothing.
///
/// A silently swallowed exception is how the build shipped installers with no payload, no
/// licence text and undiscovered dependencies — each looked like success from the outside.
/// </summary>
public class SilentFailureGuardTests
{
    /// <summary>
    /// An empty catch discards the reason something failed. Handling an expected condition is
    /// fine — but it must say so, even if only at debug level.
    /// </summary>
    [Fact]
    public void CoreLibrary_HasNoSilentlySwallowedExceptions()
    {
        var offenders = SourceFiles("Beep.Installer.Core")
            .SelectMany(FindEmptyCatches)
            .ToList();

        offenders.Should().BeEmpty(
            "every catch must record why it fired; found:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The same rule for the WinForms shell. Two teardown categories are explicitly allowed and are
    /// listed explicitly rather than filtered by a blanket pattern.
    /// </summary>
    [Fact]
    public void Shell_HasNoSilentlySwallowedExceptions()
    {
        var offenders = SourceFiles("Beep.Installer")
            .SelectMany(FindEmptyCatches)
            .Where(o =>
                // Disposal races on a form teardown: the exception TYPE is the documentation,
                // and there is nothing useful to record once the window is gone.
                !o.Contains("catch (ObjectDisposedException)") &&
                !o.Contains("catch (InvalidOperationException)"))
            .ToList();

        offenders.Should().BeEmpty(
            "every catch must record why it fired; found:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Sync-over-async deadlocks when called on a thread with a synchronization context — the
    /// installer's UI thread. Core has no exemptions.
    /// </summary>
    [Fact]
    public void CoreLibrary_DoesNotBlockOnAsync()
    {
        var offenders = SourceFiles("Beep.Installer.Core")
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, i) => (line, number: i + 1))
                .Where(x => x.line.Contains(".GetAwaiter().GetResult()"))
                .Select(x => $"{Path.GetFileName(file)}:{x.number}"))
            .ToList();

        offenders.Should().BeEmpty(
            "sync-over-async risks deadlock; found:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    private static string[] SourceFiles(string projectFolder)
    {
        var root = FindRepoSubdirectory(projectFolder);
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
    }

    /// <summary>Finds <c>catch { }</c> in code, ignoring occurrences inside comments.</summary>
    private static string[] FindEmptyCatches(string file)
    {
        var pattern = new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);

        return File.ReadAllLines(file)
            .Select((line, i) => (line, number: i + 1))
            .Where(x =>
            {
                var code = x.line.Trim();
                if (code.StartsWith("//") || code.StartsWith("///") || code.StartsWith("*")) return false;
                return pattern.IsMatch(x.line);
            })
            .Select(x => $"  {Path.GetFileName(file)}:{x.number}  {x.line.Trim()}")
            .ToArray();
    }

    private static string FindRepoSubdirectory(string name)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate '{name}' from the test output folder.");
    }
}
