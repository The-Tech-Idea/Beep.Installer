using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Models;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Guided code-signing setup.
///
/// The model carries eleven signing fields covering three <b>mutually exclusive</b> strategies — a
/// PFX file, a Windows certificate-store selector, and a remote/HSM signing service — plus a shared
/// timestamp URL. They were presented as one flat property grid, which has two consequences:
///
/// <list type="bullet">
/// <item>Nothing indicates that the groups are alternatives, so it is easy to half-fill two. And
/// <c>HasCodeSigningCertificate</c> returns true if <i>any</i> of three different roots is set, so a
/// stray value in one group silently decides which strategy the build attempts.</item>
/// <item>Nothing indicates which fields a chosen strategy actually needs, so the missing one is
/// discovered as a signing failure at build time rather than here.</item>
/// </list>
///
/// This asks for the strategy first and then only the fields that strategy uses, and clears the
/// others so the project cannot describe two at once.
/// </summary>
public sealed class CodeSigningWizard : Form
{
    /// <summary>The mutually exclusive ways this product can be signed.</summary>
    public enum SigningMethod
    {
        None,
        PfxFile,
        CertificateStore,
        RemoteService,
    }

    private readonly InstallProject _project;
    private readonly Panel _fields;
    private readonly Label _validation;
    private readonly ComboBox _method;

    // PFX
    private TextBox? _pfxPath;
    private TextBox? _pfxPassword;
    // Store
    private TextBox? _storeName;
    private TextBox? _storeLocation;
    private TextBox? _thumbprint;
    private TextBox? _subject;
    // Remote
    private TextBox? _provider;
    private TextBox? _endpoint;
    private TextBox? _keyId;
    private TextBox? _credential;
    // Shared
    private TextBox? _timestamp;

    public SigningMethod Method => (SigningMethod)(_method.SelectedItem as MethodChoice)!.Value;

    /// <summary>
    /// Everything the dialog decides, with no control attached.
    ///
    /// Named rather than positional: twelve strings in a row is an invitation to transpose two of
    /// them, and a transposed thumbprint/subject would still compile and still sign, just with the
    /// wrong certificate.
    /// </summary>
    public sealed record Choice
    {
        public SigningMethod Method { get; init; }

        public string PfxPath { get; init; } = "";
        public string PfxPassword { get; init; } = "";

        public string StoreName { get; init; } = "";
        public string StoreLocation { get; init; } = "";
        public string Thumbprint { get; init; } = "";
        public string Subject { get; init; } = "";

        public string Provider { get; init; } = "";
        public string Endpoint { get; init; } = "";
        public string KeyId { get; init; } = "";
        public string Credential { get; init; } = "";

        public string TimestampUrl { get; init; } = "";
    }

    /// <summary>What the controls say right now.</summary>
    public Choice CurrentChoice => new()
    {
        Method = Method,
        PfxPath = _pfxPath?.Text.Trim() ?? "",
        PfxPassword = _pfxPassword?.Text ?? "",
        StoreName = _storeName?.Text.Trim() ?? "",
        StoreLocation = _storeLocation?.Text.Trim() ?? "",
        Thumbprint = _thumbprint?.Text.Trim() ?? "",
        Subject = _subject?.Text.Trim() ?? "",
        Provider = _provider?.Text.Trim() ?? "",
        Endpoint = _endpoint?.Text.Trim() ?? "",
        KeyId = _keyId?.Text.Trim() ?? "",
        Credential = _credential?.Text.Trim() ?? "",
        TimestampUrl = _timestamp?.Text.Trim() ?? "",
    };

    private sealed record MethodChoice(SigningMethod Value, string Label)
    {
        public override string ToString() => Label;
    }

    public CodeSigningWizard(InstallProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        Text = L("Signing_Title", "Code signing");
        Size = new Size(720, 520);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = L("Signing_Heading", "How should this installer be signed?"),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        });

        root.Controls.Add(new Label
        {
            Text = L("Signing_Explain",
                "An unsigned installer triggers Windows SmartScreen's \"unrecognized app\" warning, which most "
                + "users read as a malware alert. These are alternatives — pick one."),
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = Ui.InstallerTheme.MutedText,
            Margin = new Padding(0, 0, 0, 12),
        });

        _method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = L("Signing_Method", "Signing method") };
        _method.Items.AddRange(new object[]
        {
            new MethodChoice(SigningMethod.None, L("Signing_None", "Do not sign (SmartScreen will warn users)")),
            new MethodChoice(SigningMethod.PfxFile, L("Signing_Pfx", "A certificate file (.pfx)")),
            new MethodChoice(SigningMethod.CertificateStore, L("Signing_Store", "A certificate in the Windows store")),
            new MethodChoice(SigningMethod.RemoteService, L("Signing_Remote", "A remote signing service or HSM")),
        });
        _method.SelectedIndex = (int)DetectCurrentMethod();
        _method.SelectedIndexChanged += (_, _) => ShowFieldsForMethod();

        var methodRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        methodRow.Controls.Add(_method);

        _fields = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _validation = new Label { AutoSize = true, ForeColor = Color.FromArgb(168, 32, 32), Margin = new Padding(0, 8, 0, 0) };

        var container = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.Controls.Add(methodRow);
        container.Controls.Add(_fields);
        container.Controls.Add(_validation);

        root.Controls.Add(container);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(20, 8, 20, 16) };
        var cancel = new Button { Text = L("Common_Cancel", "Cancel"), Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = L("Common_OK", "OK"), Width = 92, Height = 30 };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        ok.Click += (_, _) =>
        {
            var problem = CurrentProblem();
            if (problem != null) { _validation.Text = problem; return; }
            Apply();
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(root);
        Controls.Add(buttons);
        CancelButton = cancel;

        ShowFieldsForMethod();
        Engine.Accessibility.Attach(this);
    }

    /// <summary>
    /// Which strategy the project currently describes.
    ///
    /// Checked in the order <c>HasCodeSigningCertificate</c> itself uses, so the wizard opens on the
    /// strategy the build would actually attempt rather than on a different one that also has values.
    /// </summary>
    internal SigningMethod DetectCurrentMethod() => DetectMethod(_project);

    /// <summary>Which strategy the project already describes, following HasCodeSigningCertificate precedence.</summary>
    public static SigningMethod DetectMethod(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.CodeSignCertificatePath)) return SigningMethod.PfxFile;
        if (!string.IsNullOrWhiteSpace(project.CodeSignStoreThumbprint)
            || !string.IsNullOrWhiteSpace(project.CodeSignStoreSubject)) return SigningMethod.CertificateStore;
        if (!string.IsNullOrWhiteSpace(project.CodeSignRemoteEndpoint)) return SigningMethod.RemoteService;
        return SigningMethod.None;
    }

    private void ShowFieldsForMethod()
    {
        _fields.Controls.Clear();
        _validation.Text = "";
        _pfxPath = _pfxPassword = _storeName = _storeLocation = _thumbprint = _subject = null;
        _provider = _endpoint = _keyId = _credential = _timestamp = null;

        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        switch (Method)
        {
            case SigningMethod.None:
                _fields.Controls.Add(new Label
                {
                    Text = L("Signing_NoneExplain",
                        "The installer will not be signed. This is fine for internal testing; for anything you "
                        + "distribute, expect users to see a SmartScreen warning."),
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    MaximumSize = new Size(600, 0),
                    ForeColor = Ui.InstallerTheme.MutedText,
                });
                return;

            case SigningMethod.PfxFile:
                _pfxPath = Row(table, L("Field_CertificatePfx", "Certificate (.pfx):"), _project.CodeSignCertificatePath, browseFilter: "Certificates (*.pfx)|*.pfx|All files (*.*)|*.*");
                _pfxPassword = Row(table, L("Field_CertificatePassword", "Certificate password:"), _project.CodeSignCertificatePassword);
                _pfxPassword.UseSystemPasswordChar = true;
                // Named so it can be found through the public control tree rather than by reflecting
                // on a private field -- a test that pins field names breaks on a rename that changes
                // no behaviour, and would not notice the box being built but never masked.
                _pfxPassword.Name = "pfxPassword";
                Hint(table, L("Signing_PfxHint",
                    "Prefer a dpapi: or env: reference to a literal password — a password written into the "
                    + ".bsetup travels with the script into source control."));
                break;

            case SigningMethod.CertificateStore:
                _storeName = Row(table, L("Signing_StoreName", "Store name:"), string.IsNullOrWhiteSpace(_project.CodeSignStoreName) ? "My" : _project.CodeSignStoreName);
                _storeLocation = Row(table, L("Signing_StoreLocation", "Store location:"), string.IsNullOrWhiteSpace(_project.CodeSignStoreLocation) ? "CurrentUser" : _project.CodeSignStoreLocation);
                _thumbprint = Row(table, L("Signing_Thumbprint", "Thumbprint:"), _project.CodeSignStoreThumbprint);
                _subject = Row(table, L("Signing_Subject", "Or subject name:"), _project.CodeSignStoreSubject);
                Hint(table, L("Signing_StoreHint",
                    "A thumbprint identifies exactly one certificate; a subject name matches whichever one is "
                    + "installed, which can change under you when it is renewed."));
                break;

            case SigningMethod.RemoteService:
                _provider = Row(table, L("Signing_Provider", "Provider:"), _project.CodeSignRemoteProvider);
                _endpoint = Row(table, L("Signing_Endpoint", "Endpoint:"), _project.CodeSignRemoteEndpoint);
                _keyId = Row(table, L("Signing_KeyId", "Key id:"), _project.CodeSignRemoteKeyId);
                _credential = Row(table, L("Signing_Credential", "Credential:"), _project.CodeSignRemoteCredential);
                Hint(table, L("Signing_RemoteHint",
                    "The credential should be a dpapi: or env: reference. A signing credential in a script is a "
                    + "signing credential in your repository."));
                break;
        }

        _timestamp = Row(table, L("Field_TimestampURL", "Timestamp URL:"), _project.CodeSignTimestampUrl);
        Hint(table, L("Signing_TimestampHint",
            "Timestamping keeps the signature valid after the certificate expires. Without it, the installer "
            + "stops being trusted the day the certificate does."));

        _fields.Controls.Add(table);
    }

    private string? CurrentProblem() => Validate(CurrentChoice);

    /// <summary>
    /// Why this signing choice cannot be applied, or <c>null</c> if it can.
    /// </summary>
    /// <param name="fileExists">
    /// How to test for the .pfx. Injectable so the rule can be exercised without staging a real
    /// certificate on disk; defaults to the real filesystem.
    /// </param>
    public static string? Validate(Choice choice, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        switch (choice.Method)
        {
            case SigningMethod.None:
                return null;

            case SigningMethod.PfxFile:
                if (string.IsNullOrWhiteSpace(choice.PfxPath))
                    return L("Signing_NeedPfx", "Choose the .pfx file to sign with.");
                if (!fileExists(choice.PfxPath))
                    return L("Signing_PfxMissing", "That certificate file does not exist.");
                break;

            case SigningMethod.CertificateStore:
                if (string.IsNullOrWhiteSpace(choice.Thumbprint) && string.IsNullOrWhiteSpace(choice.Subject))
                    return L("Signing_NeedSelector", "Give a thumbprint or a subject name — one of them has to select the certificate.");
                break;

            case SigningMethod.RemoteService:
                if (string.IsNullOrWhiteSpace(choice.Endpoint))
                    return L("Signing_NeedEndpoint", "A remote signing service needs an endpoint.");
                if (!Uri.TryCreate(choice.Endpoint, UriKind.Absolute, out _))
                    return L("Signing_EndpointNotAbsolute", "The endpoint must be an absolute URL.");
                break;
        }

        if (!string.IsNullOrEmpty(choice.TimestampUrl) && !Uri.TryCreate(choice.TimestampUrl, UriKind.Absolute, out _))
            return L("Signing_TimestampNotAbsolute", "The timestamp URL must be absolute, such as http://timestamp.digicert.com.");

        return null;
    }

    /// <summary>
    /// Writes the chosen strategy and <b>clears the other two</b>.
    ///
    /// This is the point of the dialog. <c>HasCodeSigningCertificate</c> is true if any of three
    /// different roots is set, so leaving a stale value behind lets the project describe two
    /// strategies and the build pick whichever it checks first.
    /// </summary>
    internal void Apply() => ApplyTo(CurrentChoice, _project);

    /// <summary>
    /// Writes the chosen strategy and clears the other two. See the remarks on <see cref="Apply"/>.
    /// </summary>
    public static void ApplyTo(Choice choice, InstallProject project)
    {
        project.CodeSignCertificatePath = "";
        project.CodeSignCertificatePassword = "";
        project.CodeSignStoreName = "";
        project.CodeSignStoreLocation = "";
        project.CodeSignStoreThumbprint = "";
        project.CodeSignStoreSubject = "";
        project.CodeSignRemoteProvider = "";
        project.CodeSignRemoteEndpoint = "";
        project.CodeSignRemoteKeyId = "";
        project.CodeSignRemoteCredential = "";

        switch (choice.Method)
        {
            case SigningMethod.PfxFile:
                project.CodeSignCertificatePath = choice.PfxPath;
                project.CodeSignCertificatePassword = choice.PfxPassword;
                break;

            case SigningMethod.CertificateStore:
                project.CodeSignStoreName = choice.StoreName;
                project.CodeSignStoreLocation = choice.StoreLocation;
                project.CodeSignStoreThumbprint = choice.Thumbprint;
                project.CodeSignStoreSubject = choice.Subject;
                break;

            case SigningMethod.RemoteService:
                project.CodeSignRemoteProvider = choice.Provider;
                project.CodeSignRemoteEndpoint = choice.Endpoint;
                project.CodeSignRemoteKeyId = choice.KeyId;
                project.CodeSignRemoteCredential = choice.Credential;
                break;
        }

        project.CodeSignTimestampUrl = choice.TimestampUrl;
    }

    // ── layout helpers ──────────────────────────────────────────────────────

    private static int NextRow(TableLayoutPanel table)
    {
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table.RowCount - 1;
    }

    private static void Hint(TableLayoutPanel table, string text)
    {
        var row = NextRow(table);
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = Ui.InstallerTheme.MutedText,
            Margin = new Padding(0, 0, 0, 10),
        };
        table.Controls.Add(label, 1, row);
    }

    private TextBox Row(TableLayoutPanel table, string caption, string? value, string? browseFilter = null)
    {
        var row = NextRow(table);
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 4) }, 0, row);

        var box = new TextBox
        {
            Text = value ?? "",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            AccessibleName = caption.TrimEnd(':'),
        };
        table.Controls.Add(box, 1, row);

        if (browseFilter != null)
        {
            var button = new Button { Text = L("Btn_Browse", "Browse…"), Width = 86, Height = 24, Margin = new Padding(6, 4, 0, 4) };
            button.Click += (_, _) =>
            {
                using var dialog = new OpenFileDialog { Filter = browseFilter, Title = caption.TrimEnd(':') };
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            };
            table.Controls.Add(button, 2, row);
        }

        return box;
    }
}
