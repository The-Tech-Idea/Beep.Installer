using System.Reflection;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateCacheRetentionTests
{
    [Theory]
    [InlineData("evict")]
    [InlineData("active")]
    [InlineData("unowned")]
    [InlineData("expired")]
    public void QuotaEvictsOnlyInactiveOwnedEntries(string scenario)
    {
        var temp = Directory.CreateTempSubdirectory("BeepCacheRetention-");
        try
        {
            var first = Path.Combine(temp.FullName, new string('a', 64));
            var second = Path.Combine(temp.FullName, new string('b', 64));
            Prepare(first);
            File.WriteAllText(Path.Combine(first, DeltaUpdatePackageService.ManifestFileName), "cached metadata");
            if (scenario == "unowned") File.WriteAllText(Path.Combine(first, "user-notes.txt"), "preserve");
            using var active = scenario == "active" ? InstallationOperationLock.Acquire(first) : null;
            Action reserve = () => Prepare(second, scenario == "expired" ? 100L * 1024 * 1024 : 20L * 1024 * 1024,
                scenario == "expired" ? TimeSpan.FromTicks(1) : TimeSpan.FromDays(30));
            if (scenario is "evict" or "expired")
            {
                reserve.Should().NotThrow();
                Directory.Exists(first).Should().BeFalse();
                Directory.Exists(second).Should().BeTrue();
            }
            else
            {
                reserve.Should().Throw<TargetInvocationException>().WithInnerException<IOException>();
                Directory.Exists(first).Should().BeTrue();
                File.ReadAllText(Path.Combine(first, DeltaUpdatePackageService.ManifestFileName)).Should().Be("cached metadata");
            }
        }
        finally { temp.Delete(true); }
    }

    private static void Prepare(string entry, long maximumBytes = 20L * 1024 * 1024, TimeSpan? retention = null)
        => typeof(UpdateChannelFeedPackageService).Assembly.GetType("Beep.Installer.Engine.Updates.UpdateCacheRetention")!
            .GetMethod("Prepare")!.Invoke(null, new object[] { entry, 1L, maximumBytes, retention ?? TimeSpan.FromDays(30) });
}
