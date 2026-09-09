using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Every authorable collection has somewhere to author it.
///
/// Nine collections on <see cref="InstallProject"/> — environment variables, file associations,
/// Windows services, update channels, package nodes, prerequisite catalogs, MSIX optional packages,
/// supersedence rules and custom wizard pages — round-tripped through the serializer and were
/// honoured at install time, with no UI anywhere. The only way to use them was to hand-edit the
/// <c>.bsetup</c>, which for anyone using the tool is indistinguishable from the feature not
/// existing.
///
/// Separately, eight sections were fully written — <c>BuildCodeSignSection</c>,
/// <c>BuildMsixSection</c>, <c>BuildCompressionSection</c> and friends — with a case in
/// <c>OnSectionSelected</c> and no nav item pointing at them, so nothing could open them.
///
/// Both are the same defect in different clothes, and both are invisible to a compiler and to every
/// other test: the code is present, reachable in principle, and unreachable in practice. These
/// tests make that measurable.
/// </summary>
public class BuilderFeatureCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return dir!.FullName;
    }

    private static string BuilderSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "Beep.Installer", "Forms", "PackageBuilderForm.cs"));

    /// <summary>
    /// Collections a user is not expected to edit as a list in the builder, each with its reason.
    /// Listed rather than pattern-matched so adding one is a decision someone writes down.
    /// </summary>
    private static readonly Dictionary<string, string> EditedElsewhere = new(StringComparer.Ordinal)
    {
        ["Components"] = "has its own Components section with a feature tree",
        ["Prerequisites"] = "has its own Prerequisites section",
        ["Shortcuts"] = "has its own Shortcuts section",
        ["RegistryEntries"] = "has its own Registry section",
        ["SourceIncludes"] = "edited as patterns in the Includes section",
        ["SourceExcludes"] = "edited as patterns in the Includes section",
        ["CustomActions"] = "edited through the Custom Actions dialog on the toolbar",
        ["EnabledWizardPages"] = "edited as a checklist in the Wizard Pages section",
        ["Certificates"] = "has its own Certificates section",
        ["ComRegistrations"] = "has its own COM section",
        ["ConfigTransforms"] = "has its own Config Transforms section",
        ["DriverPackages"] = "has its own Drivers section",
        ["FirewallRules"] = "has its own Firewall Rules section",
        ["IisAppPools"] = "has its own IIS App Pools section",
        ["IisSites"] = "has its own IIS Sites section",
        ["ScheduledTasks"] = "has its own Scheduled Tasks section",
        ["WebDeployPackages"] = "has its own Web Deploy section",
    };

    public static TheoryData<string> AuthorableCollections()
    {
        var data = new TheoryData<string>();
        foreach (var property in typeof(InstallProject).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType) continue;
            if (property.PropertyType.GetGenericTypeDefinition() != typeof(ObservableCollection<>)) continue;
            data.Add(property.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AuthorableCollections))]
    public void EveryAuthorableCollectionIsReachableInTheBuilder(string collectionName)
    {
        // "Reachable" means the builder source mentions it at all: a section, a dialog, or a grid
        // binding. A collection nothing references cannot be authored except by hand-editing the
        // script, and nothing else in the suite notices.
        if (EditedElsewhere.ContainsKey(collectionName))
        {
            BuilderSource().Should().Contain(collectionName,
                $"'{collectionName}' is documented as {EditedElsewhere[collectionName]}, so the builder should still reference it");
            return;
        }

        BuilderSource().Should().Contain($"_project.{collectionName}",
            $"'{collectionName}' round-trips through the serializer and is honoured at install time, " +
            "so it needs somewhere to be authored — otherwise the only way to use it is to edit the .bsetup by hand");
    }

    [Fact]
    public void EverySectionTheBuilderCanShowHasANavItemThatOpensIt()
    {
        // The eight-orphan defect, generalised: OnSectionSelected knowing how to build a section is
        // not the same as a user being able to get to it.
        var source = BuilderSource();

        var dispatchable = System.Text.RegularExpressions.Regex
            .Matches(source, @"^\s+""(?<id>[a-z]+)""\s*=> GetOrCreate", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var offered = System.Text.RegularExpressions.Regex
            .Matches(source, @"AddItem\(\w+, ""(?<id>[a-z]+)""")
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        dispatchable.Should().NotBeEmpty("the builder must dispatch some sections");

        var unreachable = dispatchable.Except(offered).OrderBy(x => x, StringComparer.Ordinal).ToList();
        unreachable.Should().BeEmpty(
            "these sections are implemented but nothing in the nav opens them: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void EveryNavItemPointsAtSomethingThatCanBeBuilt()
    {
        // The reverse: a nav item with no dispatch case selects nothing and shows a blank pane.
        var source = BuilderSource();

        var dispatchable = System.Text.RegularExpressions.Regex
            .Matches(source, @"^\s+""(?<id>[a-z]+)""\s*=> GetOrCreate", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var offered = System.Text.RegularExpressions.Regex
            .Matches(source, @"AddItem\(\w+, ""(?<id>[a-z]+)""")
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var dangling = offered.Except(dispatchable).OrderBy(x => x, StringComparer.Ordinal).ToList();
        dangling.Should().BeEmpty(
            "these nav items open nothing: " + string.Join(", ", dangling));
    }

    [Fact]
    public void EverySectionPanelFieldIsDroppedWhenTheProjectChanges()
    {
        // New sections mean new cached panels, and a cached panel that is not dropped keeps showing
        // the previous project — and writes edits back to it. The invalidation list is derived from
        // the fields, so this asserts the derivation still sees the new ones.
        var cached = (FieldInfo[])typeof(PackageBuilderForm)
            .GetField("CachedSectionPanelFields", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        var names = cached.Select(f => f.Name).ToList();

        names.Should().Contain(new[]
        {
            "_contentEnvironmentVariables",
            "_contentFileAssociations",
            "_contentWindowsServices",
            "_contentUpdateChannels",
            "_contentPackages",
            "_contentPrerequisiteCatalogs",
            "_contentMsixOptionalPackages",
            "_contentSupersedence",
            "_contentCustomPages",
        });
    }
}
