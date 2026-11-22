using System.Collections.ObjectModel;
using System.Windows.Forms;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Ui;

/// <summary>Shared resource editor. The caller owns the model; the returned panel owns its binding.</summary>
internal static class ResourceSectionFactory
{
    internal static Panel Create<T>(
        LiveUiCaption title,
        LiveUiCaption description,
        System.Collections.ObjectModel.ObservableCollection<T> collection,
        Func<T> createDefault,
        Action changed,
        Func<T, T> clone,
        Func<T?>? createGuided = null,
        Func<T, T?>? editGuided = null,
        Func<bool>? openRecipe = null,
        Action<string, Action>? runChange = null)
        where T : class
    {
        void Change(string label, Action action)
        {
            if (runChange is null) action(); else runChange(label, action);
        }
        var p = new Panel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = InstallerTheme.Panel,
            Padding = InstallerTheme.SectionPadding
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = InstallerTheme.Panel };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Name = "SectionTitle", Text = title.Value, AutoSize = true, Font = InstallerTheme.SectionTitle, ForeColor = InstallerTheme.Text };
        var help = new Label { Name = "SectionHelp", Text = description.Value, AutoSize = true, ForeColor = InstallerTheme.MutedText, Margin = new Padding(0, 0, 0, InstallerTheme.FieldGap) };
        AuthoringThemeBinding.SetTone(help, AuthoringTone.Muted);
        title.Bind(heading); description.Bind(help);
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(help, 0, 1);
        header.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, header.ClientSize.Width - InstallerTheme.FieldGap);
            heading.MaximumSize = new Size(width, 0);
            help.MaximumSize = new Size(width, 0);
        };
        layout.Controls.Add(header, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 560 };
        var binding = new BindingSource { DataSource = collection };
        var grid = new DataGridView
        {
            AccessibleName = title.Value,
            Dock = DockStyle.Fill,
            DataSource = binding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellValueChanged += (_, _) => changed();
        grid.UserDeletedRow += (_, _) => changed();
        grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var propertyType = typeof(T).GetProperty(column.DataPropertyName)?.PropertyType;
                if (propertyType == null)
                    continue;
                if (propertyType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
                    column.Visible = false;
            }
        };

        var row = new BindingSourceRow<T>(binding);

        title.Bind(grid, text: false);
        var props = new PropertyGrid { Name = "ResourceProperties", AccessibleName = L("Resources_Properties", "Properties") + ": " + title.Value, Dock = DockStyle.Fill, HelpVisible = true };
        title.Bind(props, text: false, prefix: new("Resources_Properties", "Properties"));
        binding.CurrentChanged += (_, _) => props.SelectedObject = row.Current;
        props.SelectedObject = row.Current;
        props.PropertyValueChanged += (_, _) => changed();
        p.Disposed += (_, _) => binding.Dispose();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        var addBtn = new Button { Text = L("Btn_Add", "Add"), AutoSize = true, MinimumSize = new Size(0, InstallerTheme.ActionMinHeight) };
        var duplicateBtn = new Button { Text = L("Builder_Duplicate", "Duplicate"), AutoSize = true, MinimumSize = new Size(0, InstallerTheme.ActionMinHeight) };
        var removeBtn = new Button { Text = L("Btn_Remove", "Remove"), AutoSize = true, MinimumSize = new Size(0, InstallerTheme.ActionMinHeight) };
        new LiveUiCaption("Btn_Add", "Add").Bind(addBtn);
        new LiveUiCaption("Builder_Duplicate", "Duplicate").Bind(duplicateBtn);
        new LiveUiCaption("Btn_Remove", "Remove").Bind(removeBtn);
        addBtn.Click += (_, _) =>
        {
            var item = createDefault();
            Change("Add resource", () =>
            {
                collection.Add(item);
                binding.ResetBindings(false);
                binding.Position = collection.Count - 1;
                changed();
            });
        };
        duplicateBtn.Click += (_, _) =>
        {
            if (row.Current is not { } current)
                return;
            var item = clone(current);
            Change("Duplicate resource", () =>
            {
                collection.Add(item);
                binding.ResetBindings(false);
                binding.Position = collection.Count - 1;
                changed();
            });
        };
        removeBtn.Click += (_, _) =>
        {
            if (row.Current is not { } current)
                return;
            Change("Remove resource", () =>
            {
                collection.Remove(current);
                binding.ResetBindings(false);
                changed();
            });
        };
        btnPanel.Controls.AddRange(new Control[] { addBtn, duplicateBtn, removeBtn });

        if (openRecipe is not null)
        {
            var recipe = new Button { Name = "ResourceRecipeButton", Text = L("Recipe_Title", "Resource recipes"),
                AutoSize = true, MinimumSize = new Size(0, InstallerTheme.ActionMinHeight) };
            recipe.AccessibleName = recipe.Text;
            new LiveUiCaption("Recipe_Title", "Resource recipes").Bind(recipe);
            recipe.Click += (_, _) =>
            {
                var previousCount = collection.Count;
                var previousPosition = binding.Position;
                if (!openRecipe()) return;
                if (!p.IsDisposed)
                {
                    binding.ResetBindings(false);
                    binding.Position = collection.Count > previousCount ? collection.Count - 1 : previousPosition;
                }
            };
            btnPanel.Controls.Add(recipe);
        }

        // Nothing selected means nothing to duplicate or remove. Disabling says that plainly, rather
        // than offering a button that does nothing when pressed.
        row.WireRowButtons(duplicateBtn, removeBtn);

        // A guided path, where one exists. Typed resources are the case that needs it: "Add" gives a
        // blank operation whose arguments are a raw string dictionary, so the only way to fill it in
        // was to already know every key the provider reads.
        if (createGuided != null)
        {
            var guidedBtn = new Button { Text = L("Builder_AddGuided", "Add with wizard..."), AutoSize = true };
            new LiveUiCaption("Builder_AddGuided", "Add with wizard...").Bind(guidedBtn);
            guidedBtn.Click += (_, _) =>
            {
                if (createGuided() is not { } item) return;
                Change("Add resource", () =>
                {
                    collection.Add(item);
                    binding.ResetBindings(false);
                    binding.Position = collection.Count - 1;
                    changed();
                });
            };
            btnPanel.Controls.Add(guidedBtn);
        }

        if (editGuided != null)
        {
            var editBtn = new Button { Text = L("Builder_EditGuided", "Edit in wizard..."), AutoSize = true };
            new LiveUiCaption("Builder_EditGuided", "Edit in wizard...").Bind(editBtn);
            editBtn.Click += (_, _) =>
            {
                if (row.Current is not { } current) return;
                if (editGuided(current) is not { } replacement) return;

                var index = collection.IndexOf(current);
                if (index < 0) return;

                Change("Edit resource", () =>
                {
                    collection[index] = replacement;
                    binding.ResetBindings(false);
                    binding.Position = index;
                    changed();
                });
            };
            btnPanel.Controls.Add(editBtn);
        }

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(grid);
        var empty = new Label { Name = "ResourceEmpty", Text = L("Resources_Empty", "This list is empty. Choose Add to create an item."),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = InstallerTheme.MutedText };
        AuthoringThemeBinding.SetTone(empty, AuthoringTone.Muted);
        left.Controls.Add(empty);
        new LiveUiCaption("Resources_Empty", "This list is empty. Choose Add to create an item.").Bind(empty);
        void RefreshEmptyState()
        {
            empty.Visible = binding.Count == 0;
            grid.Visible = binding.Count > 0;
        }
        binding.ListChanged += (_, _) => RefreshEmptyState();
        RefreshEmptyState();
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(props);
        layout.Controls.Add(split, 0, 1);
        p.Controls.Add(layout);
        return p;
    }

}
