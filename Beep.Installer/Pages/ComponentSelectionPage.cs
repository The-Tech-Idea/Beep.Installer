using System;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

/// <summary>Component selection tree with checkboxes, sizes, and dependency enforcement.</summary>
public class ComponentSelectionPage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private TreeView _tree = null!;
    private ComboBox _typeCombo = null!;
    private BeepLabel _sizeLabel = null!;
    private BeepLabel _descLabel = null!;
    private BeepLabel _spaceLabel = null!;
    private bool _suppressEvents;
    private InstallContext _ctx = null!;

    public string PageTitle => "Select Components";
    public string Subtitle => "Choose which features to install.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;

    public ComponentSelectionPage(InstallContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(32, 28, 32, 12);

        _title = new BeepLabel
        {
            Text = LanguageManager.GetOrDefault("Wizard_Components", "Select Components"),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(32, 24),
            AutoSize = true
        };

        _typeCombo = new ComboBox
        {
            Location = new Point(32, 70),
            Size = new Size(180, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10)
        };
        _typeCombo.Items.AddRange(new object[] { "Typical", "Complete", "Custom" });
        _typeCombo.SelectedIndex = (int)_ctx.InstallType;
        _typeCombo.SelectedIndexChanged += OnInstallTypeChanged;

        _tree = new TreeView
        {
            Location = new Point(32, 116),
            Size = new Size(360, 320),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Segoe UI", 10)
        };

        _descLabel = new BeepLabel
        {
            Location = new Point(412, 116),
            Size = new Size(220, 100),
            AutoSize = false,
            Text = "Select a component to see its description.",
            Font = new Font("Segoe UI", 10)
        };

        _sizeLabel = new BeepLabel
        {
            Location = new Point(412, 220),
            Size = new Size(220, 24),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9)
        };

        _spaceLabel = new BeepLabel
        {
            Location = new Point(32, 440),
            Size = new Size(620, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.Gray
        };

        _tree.AfterCheck += OnNodeChecked;
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is InstallComponent comp)
            {
                _descLabel.Text = string.IsNullOrEmpty(comp.Description) ? "(no description)" : comp.Description;
                _sizeLabel.Text = Engine.LocaleFormatter.FormatSize(comp.SizeBytes);
            }
        };

        Controls.AddRange(new Control[] { _title, _typeCombo, _tree, _descLabel, _sizeLabel, _spaceLabel });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        _suppressEvents = true;
        try
        {
            // Enforce install-type defaults and hide components whose conditions don't hold.
            ComponentSelection.ApplyInstallType(ctx.Project, ctx.InstallType);
            _typeCombo.SelectedIndex = (int)ctx.InstallType;

            _tree.Nodes.Clear();
            foreach (var comp in ComponentSelection.AvailableComponents(ctx.Project))
            {
                var node = new TreeNode($"{comp.Name} ({Engine.LocaleFormatter.FormatSize(comp.SizeBytes)})")
                {
                    Tag = comp,
                    Checked = comp.Selected || comp.Required
                };
                _tree.Nodes.Add(node);
            }
        }
        finally { _suppressEvents = false; }
        UpdateSizeLabel();
    }

    private void OnInstallTypeChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || _ctx is null) return;
        _ctx.InstallType = (InstallationType)_typeCombo.SelectedIndex;

        _suppressEvents = true;
        try
        {
            ComponentSelection.ApplyInstallType(_ctx.Project, _ctx.InstallType);
            foreach (TreeNode node in _tree.Nodes)
            {
                if (node.Tag is InstallComponent c)
                    node.Checked = c.Selected || c.Required;
            }
        }
        finally { _suppressEvents = false; }
        UpdateSizeLabel();
        ValidityChanged?.Invoke(this, true);
    }

    private void OnNodeChecked(object? sender, TreeViewEventArgs e)
    {
        if (_suppressEvents) return;
        if (e.Node?.Tag is not InstallComponent comp) return;

        // Enforce required components
        if (comp.Required)
        {
            _suppressEvents = true;
            try { e.Node.Checked = true; } finally { _suppressEvents = false; }
            return;
        }

        comp.Selected = e.Node.Checked;

        _suppressEvents = true;
        try
        {
            if (!e.Node.Checked) UncheckDependents(comp);
            else CheckDependencies(comp);
        }
        finally { _suppressEvents = false; }

        UpdateSizeLabel();
        ValidityChanged?.Invoke(this, true);
    }

    private void CheckDependencies(InstallComponent comp)
    {
        if (comp.DependsOn == null) return;
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Tag is InstallComponent c && comp.DependsOn.Contains(c.Id, StringComparer.OrdinalIgnoreCase) && !node.Checked)
            {
                c.Selected = true;
                node.Checked = true;
            }
        }
    }

    private void UncheckDependents(InstallComponent comp)
    {
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Tag is InstallComponent c && c.DependsOn?.Contains(comp.Id, StringComparer.OrdinalIgnoreCase) == true)
            {
                c.Selected = false;
                node.Checked = false;
            }
        }
    }

    private void UpdateSizeLabel()
    {
        long size = 0;
        int count = 0;
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Checked && node.Tag is InstallComponent c)
            {
                size += c.SizeBytes;
                count++;
            }
        }
        _spaceLabel.Text = $"{count} component(s) selected — {Engine.LocaleFormatter.FormatSize(size)} total.";
    }

    public new bool Validate() => true;
}
