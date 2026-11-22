using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

internal static class AuthoringFileDialogs
{
    private static string ProjectFilter => $"{LanguageManager.GetOrDefault("Picker_ProjectFiles", "Beep Installer Script (*.bsetup)")}|*.bsetup|{LanguageManager.GetOrDefault("Picker_AllFiles", "All files (*.*)")}|*.*";

    internal static OpenFileDialog OpenProject() => new()
    {
        Title = LanguageManager.GetOrDefault("Builder_OpenScriptTitle", "Open Installer Script"),
        Filter = ProjectFilter,
        DefaultExt = "bsetup",
        CheckFileExists = true
    };

    internal static SaveFileDialog SaveProject(string fileName) => new()
    {
        Title = LanguageManager.GetOrDefault("Builder_SaveAsTitle", "Save As"),
        Filter = ProjectFilter,
        FileName = fileName,
        DefaultExt = "bsetup",
        AddExtension = true,
        OverwritePrompt = true
    };

    internal static string BrowseCaption(string field) => string.Format(LanguageManager.CurrentCulture,
        LanguageManager.GetOrDefault("Resource_Browse", "Browse for {0}"), field.TrimEnd(':', '：'));

    internal static OpenFileDialog File(string field, string patterns) => new()
    {
        Title = BrowseCaption(field),
        Filter = $"{string.Format(LanguageManager.CurrentCulture, LanguageManager.GetOrDefault("Picker_MatchingFiles", "Matching files ({0})"), patterns)}|{patterns}|{LanguageManager.GetOrDefault("Picker_AllFiles", "All files (*.*)")}|*.*",
        CheckFileExists = true
    };

    internal static FolderBrowserDialog Folder(string field, string? currentPath = null) => new()
    {
        Description = BrowseCaption(field),
        UseDescriptionForTitle = true,
        SelectedPath = Directory.Exists(currentPath) ? currentPath! : ""
    };
}
