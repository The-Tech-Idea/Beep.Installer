using System;
using System.Drawing;
using System.Windows.Forms;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Pages;

/// <summary>Component selection tree with checkboxes, sizes, and dependency enforcement.</summary>
public class ComponentSelectionPage : UserControl, IInstallerPage
{
    private TreeView _tree = null!;
    private Label _sizeLabel = null!;
    private Label _descLabel = null!;
    private Label _spaceLabel = null!;
    private bool _suppressEvents;

    public string PageTitle => "Select Components";
    public string Subtitle => "Choose which features to install.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;

    public ComponentSelectionPage(InstallContext ctx)
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        _tree = new TreeView
        {
            Location = new Point(0, 0),
            Size = new Size(300, 280),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        _descLabel = new Label
        {
            Location = new Point(315, 0),
            Size = new Size(210, 80),
            AutoSize = false,
            Text = "Select a component to see its description."
        };

        _sizeLabel = new Label
        {
            Location = new Point(315, 85),
            Size = new Size(210, 20),
            ForeColor = Color.Gray
        };

        _spaceLabel = new Label
        {
            Location = new Point(0, 290),
            Size = new Size(520, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
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

        Controls.AddRange(new Control[] { _tree, _descLabel, _sizeLabel, _spaceLabel });
    }

    public void OnEnter(InstallContext ctx)
    {
        _suppressEvents = true;
        try
        {
            _tree.Nodes.Clear();
            foreach (var comp in ctx.Config.Components)
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
