using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PortPilot.ViewModels;

namespace PortPilot.Views;

public partial class Pages : ResourceDictionary
{
    private static WeakReference<DataGrid>? activeGrid;
    public Pages() => InitializeComponent();
    private static MainViewModel Vm => (MainViewModel)Application.Current.MainWindow.DataContext;
    public static IReadOnlyList<InspectorRow> SelectedRows => activeGrid != null && activeGrid.TryGetTarget(out var grid) && grid.IsVisible
        ? grid.SelectedItems.Cast<InspectorRow>().ToArray() : Vm.SelectedRow == null ? [] : [Vm.SelectedRow];
    private void GridSelectionChanged(object sender, SelectionChangedEventArgs e) { if (sender is DataGrid grid && grid.IsVisible) activeGrid = new(grid); }
    private void GridRightClick(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not DataGridRow) node = VisualTreeHelper.GetParent(node);
        if (node is DataGridRow row && sender is DataGrid grid)
        {
            grid.SelectedItem = row.Item;
            Vm.SelectedRow = row.Item as InspectorRow;
            row.Focus();
        }
    }
    private async void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { e.Handled = true; await Vm.KillRowsAsync(SelectedRows, "normal"); }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Vm.CopyRows(SelectedRows); }
    }
    private void TreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is ProcessTreeNode node) Vm.SelectedRow = node.Row; }
    private void CopySelected(object sender, RoutedEventArgs e) => Vm.CopyRows(SelectedRows);
    private void FavoriteSelected(object sender, RoutedEventArgs e) => Vm.FavoriteRows(SelectedRows);
    private async void ExportSelected(object sender, RoutedEventArgs e) => await Vm.ExportRowsAsync(SelectedRows);
    private async void KillSelected(object sender, RoutedEventArgs e) => await Vm.KillRowsAsync(SelectedRows, "normal");
}
