using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

public static class ComponentSelection
{
    public static bool IsAvailable(InstallComponent component)
        => IsAvailable(component, SystemInstallerConditionFacts.Instance);

    public static bool IsAvailable(
        InstallComponent component,
        IInstallerConditionFacts conditionFacts,
        string installRoot = "",
        IReadOnlyDictionary<string, string>? variables = null)
        => InstallerConditionFactsEvaluator.Evaluate(
            component.Conditions,
            component.ConditionExpression,
            new InstallerConditionEvaluationContext
            {
                ConditionFacts = conditionFacts,
                InstallRoot = installRoot,
                Variables = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });

    public static IReadOnlyList<InstallComponent> AvailableComponents(InstallProject project)
        => project.Components.Where(IsAvailable).ToList();

    public static IReadOnlyList<InstallComponent> AvailableComponents(
        InstallProject project,
        IInstallerConditionFacts conditionFacts,
        string installRoot = "",
        IReadOnlyDictionary<string, string>? variables = null)
        => project.Components.Where(component => IsAvailable(component, conditionFacts, installRoot, variables)).ToList();

    public static void ApplyInstallType(InstallProject project, InstallationType type)
        => ApplyInstallType(project, type, SystemInstallerConditionFacts.Instance);

    public static void ApplyInstallType(
        InstallProject project,
        InstallationType type,
        IInstallerConditionFacts conditionFacts,
        string installRoot = "",
        IReadOnlyDictionary<string, string>? variables = null)
    {
        foreach (var c in project.Components)
        {
            if (!IsAvailable(c, conditionFacts, installRoot, variables))
            {
                c.Selected = false;
                continue;
            }
            c.Selected = type switch
            {
                InstallationType.Complete => true,
                InstallationType.Typical => c.Required || c.IncludedIn == InstallationType.Typical,
                _ => c.Selected || c.Required
            };
        }
    }

    public static long SelectedSize(InstallProject project)
        => SelectedSize(project, SystemInstallerConditionFacts.Instance);

    public static long SelectedSize(
        InstallProject project,
        IInstallerConditionFacts conditionFacts,
        string installRoot = "",
        IReadOnlyDictionary<string, string>? variables = null)
        => project.Components
            .Where(component => IsAvailable(component, conditionFacts, installRoot, variables) && (component.Selected || component.Required))
            .Sum(c => c.SizeBytes);
}
