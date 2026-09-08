using System;
using System.Collections.Generic;
using System.Linq;

namespace Beep.Installer.Models
{
    /// <summary>
    /// The canonical vocabulary for <c>[WizardPages]</c>.
    ///
    /// <see cref="InstallProject.EnabledWizardPages"/> round-tripped through the serializer and had
    /// a checklist in the Package Builder, but the three layers never agreed on what a page was
    /// called and nothing read the list: the builder hard-checked every box and only marked the
    /// project dirty, and the runtime wizard built all ten pages unconditionally. Turning a page off
    /// was authorable, saveable and completely inert.
    ///
    /// Ids are lowercase and stable because they are written into a human-editable <c>.bsetup</c>.
    /// <see cref="Normalize"/> also accepts the builder's display labels and a few obvious spellings
    /// so a hand-written script is not tripped up by "Start Menu" vs "startmenu".
    /// </summary>
    public static class WizardPageIds
    {
        public const string Welcome = "welcome";
        public const string License = "license";
        public const string Prerequisites = "prerequisites";
        public const string Components = "components";
        public const string Folder = "folder";
        public const string StartMenu = "startmenu";
        public const string AdditionalTasks = "tasks";
        public const string Ready = "ready";
        public const string Progress = "progress";
        public const string Complete = "complete";

        /// <summary>Every suppressible page, in the order the wizard presents them.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            Welcome, License, Prerequisites, Components, Folder,
            StartMenu, AdditionalTasks, Ready, Progress, Complete
        };

        /// <summary>
        /// Pages the wizard cannot function without. <see cref="Progress"/> is where the install
        /// actually runs and <see cref="Complete"/> is the only place a failure or a log path is
        /// reported, so an author who unchecks them gets them anyway rather than an installer that
        /// silently does nothing. They stay in the list because the checklist has always shown them.
        /// </summary>
        public static readonly IReadOnlyList<string> Structural = new[] { Progress, Complete };

        private static readonly Dictionary<string, string> Aliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["welcome"] = Welcome,
                ["license"] = License,
                ["license (eula)"] = License,
                ["eula"] = License,
                ["prerequisites"] = Prerequisites,
                ["prerequisite"] = Prerequisites,
                ["components"] = Components,
                ["component selection"] = Components,
                ["componentselection"] = Components,
                ["folder"] = Folder,
                ["destination folder"] = Folder,
                ["destinationfolder"] = Folder,
                ["destination"] = Folder,
                ["startmenu"] = StartMenu,
                ["start menu"] = StartMenu,
                ["start menu folder"] = StartMenu,
                ["group"] = StartMenu,
                ["tasks"] = AdditionalTasks,
                ["additional tasks"] = AdditionalTasks,
                ["additionaltasks"] = AdditionalTasks,
                ["ready"] = Ready,
                ["ready (review)"] = Ready,
                ["review"] = Ready,
                ["progress"] = Progress,
                ["progress (installing)"] = Progress,
                ["installing"] = Progress,
                ["complete"] = Complete,
                ["complete (launch / log)"] = Complete,
                ["finish"] = Complete
            };

        /// <summary>
        /// Maps a display label or spelling variant onto a canonical id, or returns <c>null</c> for
        /// something this build does not recognise — a page id from a newer authoring tool, say.
        /// </summary>
        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Aliases.TryGetValue(value.Trim(), out var id) ? id : null;
        }

        /// <summary>
        /// Whether <paramref name="pageId"/> should be built for this project.
        ///
        /// An empty <see cref="InstallProject.EnabledWizardPages"/> means "everything", which is what
        /// every script written before the list did anything says — the serializer only emits the
        /// section when the collection is non-empty, so absence must not mean "no pages at all".
        /// </summary>
        public static bool IsEnabled(InstallProject project, string pageId)
        {
            ArgumentNullException.ThrowIfNull(project);

            if (Structural.Contains(pageId)) return true;

            // These two predate the page list and say the same thing about their own page. An author
            // who cleared either one meant it, so they veto regardless of the checklist.
            if (pageId == Components && !project.AllowComponentSelection) return false;
            if (pageId == Folder && !project.AllowPathChange) return false;

            if (project.EnabledWizardPages.Count == 0) return true;

            foreach (var entry in project.EnabledWizardPages)
                if (string.Equals(Normalize(entry), pageId, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
