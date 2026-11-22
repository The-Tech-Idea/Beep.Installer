using Beep.Installer.Engine;
using Beep.Installer.Forms;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Ui;

internal static class TemplateGallerySelection
{
    internal static Button CreateButton(ComboBox selector)
    {
        var button = new Button { Name = "BrowseTemplateGallery", Text = L("Gallery_Title", "Template gallery"), AutoSize = true,
            MinimumSize = new(0, InstallerTheme.ActionMinHeight) };
        button.AccessibleName = button.Text;
        void Translate(object? sender, EventArgs e)
        {
            if (button.IsDisposed || button.Disposing) return;
            if (button.InvokeRequired) { button.BeginInvoke(() => Translate(sender, e)); return; }
            button.Text = button.AccessibleName = L("Gallery_Title", "Template gallery");
        }
        Lang.LanguageManager.LanguageChanged += Translate;
        button.Disposed += (_, _) => Lang.LanguageManager.LanguageChanged -= Translate;
        button.Click += (_, _) =>
        {
            using var gallery = new TemplateGalleryDialog((selector.SelectedItem as ProjectTemplate)?.Id ?? ProjectTemplates.EmptyId,
                selector.Items.Cast<ProjectTemplate>().ToArray());
            if (gallery.ShowDialog(selector.FindForm()) != DialogResult.OK) return;
            var selected = selector.Items.Cast<ProjectTemplate>().FirstOrDefault(template => template.Id.Equals(gallery.SelectedTemplateId, StringComparison.OrdinalIgnoreCase));
            if (gallery.SelectedTemplate is { Blueprint: not null } imported)
            {
                if (selected is not null) selector.Items.Remove(selected);
                selector.Items.Add(imported); selected = imported;
            }
            selector.SelectedItem = selected;
        };
        return button;
    }
}
