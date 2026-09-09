using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Guided authoring of a typed resource operation.
///
/// A <c>CompiledInstallOperation</c> carries its arguments in a string dictionary, and the builder
/// showed that dictionary raw. Composing one therefore meant already knowing the provider's type
/// string <i>and</i> every key it reads — neither of which appeared anywhere in the UI. Seventeen
/// providers were reachable in principle and unauthorable in practice: the feature existed, and the
/// only way to use it was to hand-edit the .bsetup.
///
/// This asks the two questions that actually matter — what should happen, and with what — from
/// <see cref="ResourceInputCatalog"/>, which is held to the providers themselves by
/// <c>ResourceCatalogCoverageTests</c>. Fields carry their own help, required inputs are marked,
/// choices are the ones the provider will accept, and secrets are recorded as sensitive so they stay
/// out of logs.
/// </summary>
public sealed class ResourceWizard : Form
{
    private readonly InstallProject _project;
    private readonly CompiledInstallOperation? _editing;

    private readonly Label _heading;
    private readonly Label _explain;
    private readonly Label _stepLabel;
    private readonly Label _validation;
    private readonly Panel _content;
    private readonly Button _back;
    private readonly Button _next;

    private readonly ListBox _typeList;
    private readonly Label _typeSummary;

    private int _step;
    private ResourceDraft _draft;

    /// <summary>The operation the author built, or <c>null</c> if they cancelled.</summary>
    public CompiledInstallOperation? Result { get; private set; }

    /// <summary>Every provider type on offer, in catalog order.</summary>
    public IReadOnlyList<ResourceTypeDescriptor> OfferedTypes => ResourceInputCatalog.All;

    /// <summary>The draft as it currently stands — what the controls feed.</summary>
    public ResourceDraft Draft => _draft;

    /// <summary>Two: choose the operation, then configure it.</summary>
    public int StepCount => 2;

    public int CurrentStep => _step;

    private sealed record TypeChoice(ResourceTypeDescriptor Descriptor)
    {
        public override string ToString() => Descriptor.Label;
    }

    public ResourceWizard(InstallProject project, CompiledInstallOperation? editing = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _editing = editing;

        _draft = editing != null && ResourceInputCatalog.TryGet(editing.Type, out _)
            ? ResourceDraftBuilder.FromOperation(editing)
            : ResourceDraftBuilder.CreateDefault(ResourceInputCatalog.All[0].Type);

        Text = editing == null
            ? L("Resource_Title", "Add an install operation")
            : L("Resource_EditTitle", "Edit an install operation");
        Size = new Size(820, 640);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _heading = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        root.Controls.Add(_heading);

        _explain = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(740, 0),
            ForeColor = Ui.InstallerTheme.MutedText,
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(_explain);

        _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        root.Controls.Add(_content);

        _validation = new Label
        {
            AutoSize = false,
            Height = 34,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(168, 32, 32),
            Margin = new Padding(0, 8, 0, 4),
        };
        root.Controls.Add(_validation);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _stepLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Ui.InstallerTheme.MutedText };
        _back = new Button { Text = L("Btn_Back", "< Back"), Width = 96, Height = 30 };
        _next = new Button { Text = L("Btn_Next", "Next >"), Width = 110, Height = 30 };
        var cancel = new Button
        {
            Text = L("Common_Cancel", "Cancel"),
            Width = 92,
            Height = 30,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0),
        };

        _back.Click += (_, _) => GoTo(_step - 1);
        _next.Click += (_, _) => Advance();

        buttons.Controls.Add(_stepLabel, 0, 0);
        buttons.Controls.Add(new Panel { Dock = DockStyle.Fill, Height = 1 }, 1, 0);
        buttons.Controls.Add(_back, 2, 0);

        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        right.Controls.Add(_next);
        right.Controls.Add(cancel);
        buttons.Controls.Add(right, 3, 0);
        root.Controls.Add(buttons);

        CancelButton = cancel;
        Controls.Add(root);

        // ── step 1 controls, built once so a return to step 1 keeps the selection ──
        _typeList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var descriptor in ResourceInputCatalog.All)
            _typeList.Items.Add(new TypeChoice(descriptor));

        _typeSummary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = Ui.InstallerTheme.MutedText,
            Padding = new Padding(12, 0, 0, 0),
        };

        _typeList.SelectedIndexChanged += (_, _) => OnTypeSelected();
        _typeList.SelectedIndex = Math.Max(0, ResourceInputCatalog.All
            .ToList()
            .FindIndex(d => string.Equals(d.Type, _draft.Type, StringComparison.OrdinalIgnoreCase)));

        GoTo(0);
    }

    /// <summary>The provider type currently chosen.</summary>
    public string SelectedType => ((TypeChoice)_typeList.SelectedItem!).Descriptor.Type;

    private void OnTypeSelected()
    {
        if (_typeList.SelectedItem is not TypeChoice choice) return;

        _typeSummary.Text = choice.Descriptor.Summary
                            + Environment.NewLine + Environment.NewLine
                            + string.Format(
                                L("Resource_NeedsPermissions", "Runs with: {0}"),
                                PermissionsFor(choice.Descriptor.Type));

        // Changing the answer to "what should happen" invalidates the answers to "with what".
        if (!string.Equals(choice.Descriptor.Type, _draft.Type, StringComparison.OrdinalIgnoreCase))
            _draft = ResourceDraftBuilder.CreateDefault(choice.Descriptor.Type);
    }

    /// <summary>What the provider declares it needs, read from the provider rather than restated.</summary>
    internal static string PermissionsFor(string type)
    {
        var provider = BuiltInInstallerResourceProviders.CreateDefaultRegistry()
            .Providers.FirstOrDefault(p => string.Equals(p.ResourceType, type, StringComparison.OrdinalIgnoreCase));

        if (provider == null) return "";

        var permissions = provider.RequiredPermissions;
        return permissions == InstallerExtensionPermission.None
            ? L("Resource_NoPermissions", "no special permissions")
            : permissions.ToString();
    }

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, StepCount - 1);
        _validation.Text = "";
        _content.Controls.Clear();

        if (_step == 0)
        {
            _heading.Text = L("Resource_ChooseHeading", "What should the installer do?");
            _explain.Text = L("Resource_ChooseExplain",
                "Each of these is executed at install time by a typed provider, with rollback if a later "
                + "step fails.");
            _content.Controls.Add(BuildTypeStep());
        }
        else
        {
            ResourceInputCatalog.TryGet(SelectedType, out var descriptor);
            _heading.Text = descriptor.Label;
            _explain.Text = descriptor.Summary;
            _content.Controls.Add(BuildFieldsStep(descriptor));
        }

        _back.Enabled = _step > 0;
        _next.Text = _step == StepCount - 1
            ? L("Resource_Add", "Add")
            : L("Btn_Next", "Next >");
        _stepLabel.Text = string.Format(L("Quick_StepOf", "Step {0} of {1}"), _step + 1, StepCount);
    }

    private Control BuildTypeStep()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        split.Controls.Add(_typeList, 0, 0);
        split.Controls.Add(_typeSummary, 1, 0);
        OnTypeSelected();
        return split;
    }

    private Control BuildFieldsStep(ResourceTypeDescriptor descriptor)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        foreach (var input in descriptor.Inputs)
            AddField(table, input, () => _draft.Value(input.Key), v => _draft.Values[input.Key] = v);

        foreach (var group in descriptor.Repeatables)
            AddRepeatable(table, group);

        return table;
    }

    private void AddField(TableLayoutPanel table, ResourceInput input, Func<string> get, Action<string> set)
    {
        var row = NextRow(table);

        var caption = new Label
        {
            Text = input.Label + (input.Required ? " *" : ""),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 4),
        };
        table.Controls.Add(caption, 0, row);

        Control editor = input.Kind switch
        {
            ResourceInputKind.Bool => BoolEditor(get, set),
            ResourceInputKind.Choice => ChoiceEditor(input, get, set),
            _ => TextEditor(input, get, set),
        };

        editor.Name = input.Key;                 // found by name, not by reflecting on a field
        editor.AccessibleName = input.Label;
        table.Controls.Add(editor, 1, row);

        if (!string.IsNullOrWhiteSpace(input.Help))
        {
            table.Controls.Add(new Label
            {
                Text = input.Help,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                ForeColor = Ui.InstallerTheme.MutedText,
                Margin = new Padding(0, 0, 0, 8),
            }, 1, NextRow(table));
        }
    }

    private static Control BoolEditor(Func<string> get, Action<string> set)
    {
        var box = new CheckBox
        {
            Checked = bool.TryParse(get(), out var b) && b,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        box.CheckedChanged += (_, _) => set(box.Checked ? "true" : "false");
        return box;
    }

    private static Control ChoiceEditor(ResourceInput input, Func<string> get, Action<string> set)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            Margin = new Padding(0, 5, 0, 4),
        };

        foreach (var option in input.Options)
            combo.Items.Add(option);

        var current = get();
        combo.SelectedIndex = Math.Max(0, input.Options
            .ToList()
            .FindIndex(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase)));

        combo.SelectedIndexChanged += (_, _) => set((string)(combo.SelectedItem ?? ""));
        return combo;
    }

    private Control TextEditor(ResourceInput input, Func<string> get, Action<string> set)
    {
        var box = new TextBox
        {
            Text = get(),
            Width = input.Kind == ResourceInputKind.MultiLine ? 460 : 380,
            Multiline = input.Kind == ResourceInputKind.MultiLine,
            Height = input.Kind == ResourceInputKind.MultiLine ? 60 : 24,
            UseSystemPasswordChar = input.Kind == ResourceInputKind.Secret,
            Margin = new Padding(0, 5, 0, 4),
        };
        box.TextChanged += (_, _) => set(box.Text);

        if (input.Kind is not (ResourceInputKind.FilePath or ResourceInputKind.FolderPath))
            return box;

        var host = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        box.Width = 340;
        host.Controls.Add(box);

        var browse = new Button { Text = "...", Width = 34, Height = 26, Margin = new Padding(4, 5, 0, 4) };
        browse.AccessibleName = string.Format(L("Resource_Browse", "Browse for {0}"), input.Label);
        browse.Click += (_, _) =>
        {
            if (input.Kind == ResourceInputKind.FolderPath)
            {
                using var picker = new FolderBrowserDialog { SelectedPath = box.Text };
                if (picker.ShowDialog(this) == DialogResult.OK) box.Text = picker.SelectedPath;
                return;
            }

            using var open = new OpenFileDialog { CheckFileExists = false, FileName = box.Text };
            if (open.ShowDialog(this) == DialogResult.OK) box.Text = open.FileName;
        };
        host.Controls.Add(browse);
        return host;
    }

    /// <summary>A repeatable group, rendered as a grid with add/remove.</summary>
    private void AddRepeatable(TableLayoutPanel table, ResourceInputList group)
    {
        var row = NextRow(table);
        table.Controls.Add(new Label
        {
            Text = group.Label,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 14, 8, 4),
        }, 0, row);

        var host = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 4),
        };

        var grid = new DataGridView
        {
            Width = 460,
            Height = 120,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Name = group.Prefix,
            AccessibleName = group.Label,
        };

        foreach (var field in group.Fields)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = field.Key,
                HeaderText = field.Label,
                DataPropertyName = field.Key,
            });
        }

        void Reload()
        {
            grid.Rows.Clear();
            foreach (var record in _draft.Rows(group.Prefix))
                grid.Rows.Add(group.Fields.Select(f => record.TryGetValue(f.Key, out var v) ? v : "").Cast<object>().ToArray());
        }

        grid.CellEndEdit += (_, e) =>
        {
            var records = _draft.Rows(group.Prefix);
            if (e.RowIndex < 0 || e.RowIndex >= records.Count) return;
            records[e.RowIndex][group.Fields[e.ColumnIndex].Key] =
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
        };

        var add = new Button { Text = L("Common_Add", "Add"), Width = 90, Height = 26 };
        add.Click += (_, _) =>
        {
            _draft.Rows(group.Prefix).Add(ResourceDraftBuilder.CreateRow(group));
            Reload();
        };

        var remove = new Button { Text = L("Common_Remove", "Remove"), Width = 90, Height = 26, Margin = new Padding(6, 3, 0, 3) };
        remove.Click += (_, _) =>
        {
            if (grid.CurrentRow == null) return;
            var index = grid.CurrentRow.Index;
            var records = _draft.Rows(group.Prefix);
            if (index >= 0 && index < records.Count) records.RemoveAt(index);
            Reload();
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);

        host.Controls.Add(grid);
        host.Controls.Add(buttons);

        if (!string.IsNullOrWhiteSpace(group.Help))
        {
            host.Controls.Add(new Label
            {
                Text = group.Help,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                ForeColor = Ui.InstallerTheme.MutedText,
            });
        }

        table.Controls.Add(host, 1, row);
        Reload();
    }

    private void Advance()
    {
        if (_step < StepCount - 1)
        {
            GoTo(_step + 1);
            return;
        }

        var problems = ResourceDraftBuilder.Validate(_draft);
        if (problems.Count > 0)
        {
            _validation.Text = string.Join(Environment.NewLine, problems.Take(3).Select(p => p.Message));
            FocusField(problems[0].Key);
            return;
        }

        Result = ResourceDraftBuilder.Build(
            _draft,
            _editing != null ? _editing.Id : ResourceDraftBuilder.NextId(_draft.Type, _project.Resources));

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Puts the caret on the field that was reported, rather than only naming it.</summary>
    private void FocusField(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var match = _content.Controls.Find(key, searchAllChildren: true).FirstOrDefault();
        match?.Focus();
    }

    private static int NextRow(TableLayoutPanel table)
    {
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table.RowCount - 1;
    }
}
