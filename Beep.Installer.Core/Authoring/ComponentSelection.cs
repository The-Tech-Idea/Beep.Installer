using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

public static class ComponentSelection
{
    public static bool IsAvailable(InstallComponent component)
        => InstallConditionEvaluator.EvaluateAll(component.Conditions);

    public static IReadOnlyList<InstallComponent> AvailableComponents(InstallProject project)
        => project.Components.Where(IsAvailable).ToList();

    public static void ApplyInstallType(InstallProject project, InstallationType type)
    {
        foreach (var c in project.Components)
        {
            if (!IsAvailable(c))
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
        => project.Components.Where(c => c.Selected || c.Required).Sum(c => c.SizeBytes);
}