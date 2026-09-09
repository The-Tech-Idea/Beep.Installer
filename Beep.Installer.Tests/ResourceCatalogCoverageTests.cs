using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Beep.Installer.Extensibility;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The catalog that makes typed resources authorable is held to the providers themselves.
///
/// <see cref="ResourceInputCatalog"/> exists so the builder can render a real editor for an
/// operation instead of a raw <c>SortedDictionary&lt;string, string&gt;</c>. That is only worth
/// anything if it describes the keys the providers actually read — a catalog that drifts is worse
/// than none, because it invites the author to fill in a key nothing consumes.
///
/// So both directions are checked against the provider sources: every key a provider reads is
/// either described or explicitly marked provider-managed, and every key the catalog describes is
/// really read somewhere. The second direction is the one that catches a typo in the catalog, which
/// no amount of using the wizard would reveal.
/// </summary>
public class ResourceCatalogCoverageTests
{
    /// <summary>
    /// Every way a provider reads an input: the Input/BoolInput/IntInput helpers, the indexer, and
    /// the dictionary probes. FileCopyResourceProvider uses only the last of those, so a scanner
    /// that missed them would have declared its two required keys unread.
    /// </summary>
    private static readonly Regex ScalarRead =
        new(@"(?:Bool|Int)?Input\(operation,\s*""([A-Za-z][A-Za-z0-9.]*)""" +
            @"|Inputs\[""([A-Za-z][A-Za-z0-9.]*)""\]" +
            @"|Inputs\.(?:TryGetValue|ContainsKey)\(""([A-Za-z][A-Za-z0-9.]*)""",
            RegexOptions.Compiled);

    /// <summary>Input(operation, $"binding.{i}.protocol") — a field of a repeatable group.</summary>
    private static readonly Regex ListRead =
        new(@"(?:Bool|Int)?Input\(operation,\s*\$""([A-Za-z]+)\.\{i\}\.([A-Za-z][A-Za-z0-9]*)""",
            RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return dir!;
    }

    private sealed record ProviderSource(string ResourceType, HashSet<string> ScalarKeys, HashSet<string> ListKeys);

    /// <summary>
    /// Every provider class and the keys it reads.
    ///
    /// Split per class rather than per file: IisResourceProviders.cs holds two providers, and
    /// merging their keys would let each one claim the other's.
    /// </summary>
    private static List<ProviderSource> ScanProviders()
    {
        var dir = Path.Combine(RepoRoot().FullName, "Beep.Installer.Core", "Extensibility", "Providers");
        Directory.Exists(dir).Should().BeTrue();

        var found = new List<ProviderSource>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            var source = File.ReadAllText(file);

            foreach (var chunk in Regex.Split(source, @"\n(?=(?:public|internal)\s+(?:sealed\s+)?class\s)"))
            {
                var type = Regex.Match(chunk, @"ResourceType\s*=>\s*""([^""]+)""");
                if (!type.Success) continue;

                var scalars = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in ScalarRead.Matches(chunk))
                {
                    var group = m.Groups.Cast<Group>().Skip(1).First(g => g.Success);
                    scalars.Add(group.Value);
                }

                var lists = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in ListRead.Matches(chunk))
                    lists.Add(m.Groups[1].Value + "." + m.Groups[2].Value);

                found.Add(new ProviderSource(type.Groups[1].Value, scalars, lists));
            }
        }

        return found;
    }

    [Fact]
    public void TheScannerFindsTheProviders()
    {
        // Anti-vacuity. If the regexes stopped matching, every coverage assertion below would pass
        // against nothing at all.
        var providers = ScanProviders();
        var registered = BuiltInInstallerResourceProviders.CreateDefaultRegistry()
            .Providers.Select(p => p.ResourceType);

        // Derived, not a magic number: a provider added tomorrow is scanned or this fails.
        providers.Select(p => p.ResourceType).Should().BeEquivalentTo(registered);

        providers.Should().Contain(p => p.ResourceType == "file.copy" && p.ScalarKeys.Contains("destination"));
        providers.Should().Contain(p => p.ResourceType == "iis.site" && p.ListKeys.Contains("binding.protocol"));
    }

    [Fact]
    public void EveryBuiltInProviderIsDescribed()
    {
        var registered = BuiltInInstallerResourceProviders.CreateDefaultRegistry()
            .Providers.Select(p => p.ResourceType).ToList();

        registered.Should().NotBeEmpty();

        var undescribed = registered
            .Where(t => !ResourceInputCatalog.TryGet(t, out _))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        undescribed.Should().BeEmpty(
            "these providers would still be authored as a raw dictionary: " + string.Join(", ", undescribed));
    }

    [Fact]
    public void TheCatalogDescribesNothingThatIsNotAProvider()
    {
        var registered = BuiltInInstallerResourceProviders.CreateDefaultRegistry()
            .Providers.Select(p => p.ResourceType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var phantom = ResourceInputCatalog.All
            .Select(d => d.Type)
            .Where(t => !registered.Contains(t))
            .ToList();

        phantom.Should().BeEmpty(
            "the wizard would offer an operation type nothing can execute: " + string.Join(", ", phantom));
    }

    [Fact]
    public void EveryKeyAProviderReadsIsDescribedOrDeclaredProviderManaged()
    {
        var problems = new List<string>();

        foreach (var provider in ScanProviders())
        {
            if (!ResourceInputCatalog.TryGet(provider.ResourceType, out var descriptor)) continue;

            var described = descriptor.Inputs.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            foreach (var key in provider.ScalarKeys)
            {
                if (described.Contains(key)) continue;
                if (ResourceInputCatalog.IsNonAuthored(key)) continue;
                problems.Add($"{provider.ResourceType} reads '{key}' but nothing describes it");
            }

            foreach (var listKey in provider.ListKeys)
            {
                var prefix = listKey[..listKey.IndexOf('.')];
                var field = listKey[(listKey.IndexOf('.') + 1)..];

                var group = descriptor.Repeatables.FirstOrDefault(g => g.Prefix == prefix);
                if (group == null)
                {
                    problems.Add($"{provider.ResourceType} reads '{prefix}.N.*' but has no described list");
                    continue;
                }

                if (group.Fields.All(f => f.Key != field))
                    problems.Add($"{provider.ResourceType} reads '{prefix}.N.{field}' but the list does not describe it");
            }
        }

        problems.Should().BeEmpty(string.Join("; ", problems));
    }

    [Fact]
    public void EveryKeyTheCatalogDescribesIsActuallyRead()
    {
        // The direction that catches a typo in the catalog. A described key nothing reads is a field
        // the author fills in for no effect -- invisible from the UI, and permanent.
        var byType = ScanProviders()
            .GroupBy(p => p.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (Scalars: g.SelectMany(p => p.ScalarKeys).ToHashSet(StringComparer.Ordinal),
                      Lists: g.SelectMany(p => p.ListKeys).ToHashSet(StringComparer.Ordinal)),
                StringComparer.OrdinalIgnoreCase);

        var problems = new List<string>();

        foreach (var descriptor in ResourceInputCatalog.All)
        {
            byType.TryGetValue(descriptor.Type, out var read).Should().BeTrue(
                $"'{descriptor.Type}' should have been scanned");

            foreach (var input in descriptor.Inputs)
            {
                if (!read.Scalars.Contains(input.Key))
                    problems.Add($"{descriptor.Type}.{input.Key} is described but never read");
            }

            foreach (var group in descriptor.Repeatables)
            {
                foreach (var field in group.Fields)
                {
                    if (!read.Lists.Contains(group.Prefix + "." + field.Key))
                        problems.Add($"{descriptor.Type}.{group.Prefix}.N.{field.Key} is described but never read");
                }
            }
        }

        problems.Should().BeEmpty(string.Join("; ", problems));
    }

    [Fact]
    public void RequiredInputsAreTheOnesValidationActuallyDemands()
    {
        // A descriptor that marks the wrong field required makes the wizard refuse a valid operation
        // or accept one the provider will reject. Spot-checked against the providers' own messages.
        ResourceInputCatalog.TryGet("shortcut.create", out var shortcut).Should().BeTrue();
        shortcut.Inputs.Where(i => i.Required).Select(i => i.Key)
            .Should().BeEquivalentTo(new[] { "name", "targetPath" },
                "those are the two the provider reports BI3301/BI3302 for");

        ResourceInputCatalog.TryGet("file.copy", out var copy).Should().BeTrue();
        copy.Inputs.Where(i => i.Required).Select(i => i.Key)
            .Should().BeEquivalentTo(new[] { "source", "destination" });
    }

    [Fact]
    public void ChoiceInputsOfferSomethingToChoose()
    {
        var empty = ResourceInputCatalog.All
            .SelectMany(d => d.Inputs.Select(i => (d.Type, Input: i)))
            .Concat(ResourceInputCatalog.All.SelectMany(d => d.Repeatables.SelectMany(g => g.Fields.Select(f => (d.Type, Input: f)))))
            .Where(x => x.Input.Kind == ResourceInputKind.Choice && x.Input.Options.Count == 0)
            .Select(x => $"{x.Type}.{x.Input.Key}")
            .ToList();

        empty.Should().BeEmpty("a choice with no options is an empty dropdown: " + string.Join(", ", empty));
    }

    [Fact]
    public void EnumBackedChoicesTrackTheEnumRatherThanACopyOfIt()
    {
        // The point of ChoiceEnum: the offered list is read from the type the provider parses into,
        // so adding a member to the enum cannot leave the wizard offering a stale list.
        ResourceInputCatalog.TryGet("shortcut.create", out var shortcut).Should().BeTrue();
        var location = shortcut.Inputs.Single(i => i.Key == "location");

        location.Options.Should().BeEquivalentTo(Enum.GetNames<TheTechIdea.Beep.Installer.ShortcutLocation>());
    }

    [Fact]
    public void EveryDescribedInputHasALabel()
    {
        var unlabelled = ResourceInputCatalog.All
            .SelectMany(d => d.Inputs.Select(i => (d.Type, i.Key, i.Label)))
            .Where(x => string.IsNullOrWhiteSpace(x.Label))
            .Select(x => $"{x.Type}.{x.Key}")
            .ToList();

        unlabelled.Should().BeEmpty("an unlabelled field is the raw dictionary again: " + string.Join(", ", unlabelled));
    }
}
