using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Ui;

namespace Beep.Installer.Forms;

/// <summary>Selects the authoring UI culture independently of the authored installer.</summary>
internal sealed class AuthoringLanguageDialog : Form
{
    private sealed record Choice(string Code)
    {
        public override string ToString() => LanguageManager.NativeNameOf(Code);
    }

    private readonly ComboBox _languages = new() { Name = "AuthoringUiLanguage", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly Label _notice = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button _apply = new() { Name = "AuthoringApplyLanguage", AutoSize = true };
    private readonly Button _edit = new() { Name = "AuthoringEditTranslations", AutoSize = true };
    private readonly Button _close = new() { Name = "AuthoringCloseLanguages", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly AuthoringThemeBinding _theme;

    internal AuthoringLanguageDialog()
    {
        Name = "AuthoringLanguages";
        Size = new(570, 280); MinimumSize = new(420, 240);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Font = InstallerTheme.Body;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 3, AutoScroll = true };
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        _notice.Margin = new(3, 12, 3, 12);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange(new Control[] { _apply, _edit, _close });
        layout.Controls.Add(_languages, 0, 0); layout.Controls.Add(_notice, 0, 1); layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        _languages.Items.AddRange(LanguageManager.SupportedCultures.Select(code => new Choice(code)).ToArray());
        _languages.SelectedItem = _languages.Items.Cast<Choice>().Single(choice => choice.Code == LanguageManager.CurrentCulture.TwoLetterISOLanguageName);
        _apply.Click += (_, _) =>
        {
            if (_languages.SelectedItem is Choice choice) LanguageManager.SetLanguage(choice.Code);
        };
        _edit.Click += (_, _) => { using var editor = new LanguageManagerForm(); editor.ShowDialog(this); };
        AcceptButton = _apply; CancelButton = _close;
        _theme = new AuthoringThemeBinding(this);
        LanguageManager.LanguageChanged += OnLanguageChanged;
        Translate();
        Engine.Accessibility.Attach(this);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        Translate();
    }

    private void Translate()
    {
        var focus = UiFocusBookmark.Capture(this);
        Text = LanguageManager.GetString("AuthoringLanguage_Title");
        _languages.AccessibleName = Text;
        _notice.Text = _notice.AccessibleName = LanguageManager.GetString("AuthoringLanguage_Help");
        _apply.Text = _apply.AccessibleName = LanguageManager.GetString("Btn_Apply");
        _edit.Text = _edit.AccessibleName = LanguageManager.GetString("AuthoringLanguage_Edit");
        _close.Text = _close.AccessibleName = LanguageManager.GetString("Common_Close");
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft;
        focus?.Restore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { LanguageManager.LanguageChanged -= OnLanguageChanged; _theme.Dispose(); }
        base.Dispose(disposing);
    }
}
