using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Beep.Installer.Forms;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Cached builder sections are dropped when the project changes (6.x dead/stale UI).
///
/// The Package Builder caches one <see cref="Panel"/> per left-nav section and rebuilds it lazily.
/// Opening a different project has to drop those panels, because each one is data-bound to the
/// collections of the project it was built for. The drop list was written by hand and had fallen
/// nine sections behind: certificates, COM registrations, config transforms, driver packages,
/// firewall rules, both IIS sections, scheduled tasks and web-deploy packages were never cleared,
/// so they kept displaying the previous project — and edits made in them were written back to it.
/// </summary>
public class BuilderSectionCacheTests
{
    // _contentHost is deliberately excluded: it is the live container the cached panels dock into,
    // not one of them, and the "_content" prefix convention matches its name too. Disposing and
    // nulling it here crashed the very next line of OnProjectReloaded (HideWelcome's
    // _contentHost.Controls.Clear()) the moment a second project loaded into an already-open builder.
    private static FieldInfo[] SectionPanelFields =>
        typeof(PackageBuilderForm)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Panel)
                        && f.Name.StartsWith("_content", StringComparison.Ordinal)
                        && f.Name != "_contentHost")
            .ToArray();

    [Fact]
    public void EverySectionPanelFieldIsCovered_BecauseTheListIsNotWrittenByHand()
    {
        // The cache-invalidation list is derived from the fields themselves, so this asserts the
        // derivation still finds them rather than re-checking a hand-maintained list.
        var cached = typeof(PackageBuilderForm)
            .GetField("CachedSectionPanelFields", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as FieldInfo[];

        cached.Should().NotBeNull("InvalidateAllContent must enumerate the section fields");
        cached!.Select(f => f.Name).Should().BeEquivalentTo(
            SectionPanelFields.Select(f => f.Name),
            "every cached section panel has to be dropped when the project changes");
    }

    [Fact]
    public void TheSectionsThatUsedToBeMissed_AreCoveredNow()
    {
        // Named explicitly: these are the nine that silently showed a closed project's data.
        var cached = (FieldInfo[])typeof(PackageBuilderForm)
            .GetField("CachedSectionPanelFields", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var names = cached.Select(f => f.Name).ToArray();

        names.Should().Contain(new[]
        {
            "_contentCertificates",
            "_contentComRegistrations",
            "_contentConfigTransforms",
            "_contentDriverPackages",
            "_contentFirewallRules",
            "_contentIisAppPools",
            "_contentIisSites",
            "_contentScheduledTasks",
            "_contentWebDeployPackages"
        });
    }

    [Fact]
    public void ThereIsAtLeastOneSectionToInvalidate()
    {
        // Guards the reflection predicate itself: a rename of the _content prefix would otherwise
        // turn this whole mechanism into a silent no-op and the tests above into vacuous truths.
        SectionPanelFields.Should().NotBeEmpty();
    }

    [Fact]
    public void ContentHostItselfIsNeverInvalidated()
    {
        // Regression: _contentHost matched the same "_content" prefix as every cached section panel,
        // so InvalidateAllContent disposed and nulled the live container itself, not just the panels
        // docked into it -- crashing the next line of OnProjectReloaded on the very first project
        // reload against an already-open builder (e.g. File > Open with a project already open).
        var cached = (FieldInfo[])typeof(PackageBuilderForm)
            .GetField("CachedSectionPanelFields", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        cached.Select(f => f.Name).Should().NotContain("_contentHost");
    }
}
