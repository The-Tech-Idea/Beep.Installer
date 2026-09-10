using System.Windows.Forms;

namespace Beep.Installer.Ui;

/// <summary>
/// Safe "current row" access for a <see cref="BindingSource"/>, plus the row-button enablement every
/// list-editing dialog/section in the builder needs.
///
/// <see cref="BindingSource.Current"/> throws on an empty list -- the underlying
/// <c>CurrencyManager.Current</c> does not return null when there is no current row, it throws
/// <see cref="System.IndexOutOfRangeException"/>("Index -1 does not have a value"). Every
/// <c>binding.Current is not T</c> guard written against that assumption never gets the chance to run
/// on an empty collection: Remove/Duplicate blow up instead of being a no-op. This reads the row by
/// position instead, and wires the "disable when nothing is selected" behavior that should go with it,
/// once, rather than every dialog reimplementing (or forgetting) the fix.
/// </summary>
public sealed class BindingSourceRow<T> where T : class
{
    private readonly BindingSource _binding;

    public BindingSourceRow(BindingSource binding) => _binding = binding;

    /// <summary>The selected row, or null. Never throws, even when the list is empty.</summary>
    public T? Current => _binding.Position >= 0 && _binding.Position < _binding.Count
        ? _binding[_binding.Position] as T
        : null;

    public bool HasRow => Current != null;

    /// <summary>
    /// Disables <paramref name="rowButtons"/> whenever there is no current row, rather than offering an
    /// action that cannot work. Call once after the buttons are created.
    /// </summary>
    public void WireRowButtons(params Control[] rowButtons)
    {
        void Refresh()
        {
            var hasRow = HasRow;
            foreach (var button in rowButtons)
                button.Enabled = hasRow;
        }

        _binding.PositionChanged += (_, _) => Refresh();
        _binding.ListChanged += (_, _) => Refresh();
        Refresh();
    }
}
