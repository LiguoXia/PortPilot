using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using PortPilot.ViewModels;

namespace PortPilot.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent(); ViewModel = vm; DataContext = vm;
        vm.PropertyChanged += VmChanged; Closed += (_, _) => { vm.PropertyChanged -= VmChanged; vm.Dispose(); };
        UpdateNavigation();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); var corner = 2;
        var result = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref corner, sizeof(int));
        if (result != 0) System.Diagnostics.Trace.WriteLine($"Rounded corners unavailable: 0x{result:X8}");
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page))
        {
            UpdateNavigation();
            if (SystemParameters.ClientAreaAnimation) PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(150)));
        }
        if (e.PropertyName == nameof(MainViewModel.HasDetails) && ViewModel.HasDetails && SystemParameters.ClientAreaAnimation)
            DetailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        if (e.PropertyName == nameof(MainViewModel.IsPaletteOpen) && ViewModel.IsPaletteOpen) Dispatcher.BeginInvoke(() => { PaletteSearch.Focus(); PaletteList.SelectedIndex = 0; });
    }
    private void UpdateNavigation()
    {
        foreach (var button in new[] {DashboardNav, PortsNav, ProcessesNav, ConnectionsNav, FavoritesNav, HistoryNav, SettingsNav})
        {
            var selected = (string)button.CommandParameter == ViewModel.Page;
            if (selected) { button.SetResourceReference(BackgroundProperty, "SelectionBrush"); button.SetResourceReference(ForegroundProperty, "AccentBrush"); }
            else { button.Background = System.Windows.Media.Brushes.Transparent; button.SetResourceReference(ForegroundProperty, "TextBrush"); }
        }
    }
    public void FocusSearch() { SearchBox.Focus(); SearchBox.CaretIndex = SearchBox.Text.Length; }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ViewModel.SubmitSearchCommand.Execute(null); e.Handled = true; } }
    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Escape) { ViewModel.ClosePaletteCommand.Execute(null); ViewModel.CloseDetailsCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { FocusSearch(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F5 || ctrl && e.Key == Key.R) { await ViewModel.RefreshAsync(); e.Handled = true; }
        else if (ctrl && e.Key == Key.K) { ViewModel.OpenPaletteCommand.Execute(null); e.Handled = true; }
        else if (ViewModel.IsPaletteOpen && e.Key == Key.Down && PaletteSearch.IsKeyboardFocusWithin) { PaletteList.Focus(); PaletteList.SelectedIndex = 0; e.Handled = true; }
        else if (ViewModel.IsPaletteOpen && e.Key == Key.Enter) { await ViewModel.RunPaletteCommand.ExecuteAsync(PaletteList.SelectedItem ?? ViewModel.FilteredPaletteCommands.FirstOrDefault()); e.Handled = true; }
        else if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox) { e.Handled = true; await ViewModel.KillRowsAsync(Pages.SelectedRows, "normal"); }
    }
    private async void PaletteKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && PaletteList.SelectedItem is string command) { e.Handled = true; await ViewModel.RunPaletteCommand.ExecuteAsync(command); } }
    private async void PaletteDoubleClick(object sender, MouseButtonEventArgs e) { if (PaletteList.SelectedItem is string command) await ViewModel.RunPaletteCommand.ExecuteAsync(command); }
}
