using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortPilot.Core.Services;
using PortPilot.Services;
using PortPilot.ViewModels;
using PortPilot.Views;

namespace PortPilot.Infrastructure;

internal static class SearchUiSmoke
{
    public static async Task VerifyAsync(MainWindow window, ThemeService themes, string folder, int fixturePort, List<string> results)
    {
        var vm = window.ViewModel;
        vm.HasDetails = false; vm.NavigateCommand.Execute("Ports");
        vm.SearchPortCommand.Execute(fixturePort.ToString());
        vm.NavigateCommand.Execute("Dashboard");
        var resets = 0;
        NotifyCollectionChangedEventHandler changed = (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        foreach (var view in new[] { vm.PortsView, vm.ConnectionsView, vm.ProcessesView }) view.CollectionChanged += changed;
        try
        {
            window.SearchFieldPicker.SelectedValue = SearchField.ProcessName;
            window.SearchExactToggle.IsChecked = true;
            window.SearchBox.Text = "";
            foreach (var character in "不存在的中文进程.exe")
            {
                window.SearchBox.Text += character;
                await Task.Delay(300); // Cross the old debounce interval after every keystroke.
            }
            await vm.RefreshAsync();
            if (resets != 0 || vm.Page != "Dashboard" || !vm.HasPendingSearch || !vm.CanFavoriteQuery
                || !vm.PortsView.Cast<InspectorRow>().Any(r => r.Port == fixturePort)
                || vm.PortsView.Cast<InspectorRow>().Any(r => r.Port != fixturePort))
                throw new InvalidOperationException("Typing/options/refresh executed a draft query or reset the displayed results.");
            vm.SetFilterCommand.Execute("TCP");
            if (!vm.PortsView.Cast<InspectorRow>().Any(r => r.Port == fixturePort))
                throw new InvalidOperationException("Changing a filter applied unsubmitted search text.");
            vm.SetFilterCommand.Execute("All");
            results.Add("Search draft: slow Chinese typing + field/exact changes cause zero view resets or navigation; refresh/filter preserve submitted query: PASS");
        }
        finally { foreach (var view in new[] { vm.PortsView, vm.ConnectionsView, vm.ProcessesView }) view.CollectionChanged -= changed; }
        window.SearchFieldPicker.SelectedValue = SearchField.Pid;
        window.SearchBox.Text = Environment.ProcessId.ToString();
        window.SearchBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        await WaitForAsync(() => vm.SelectedSearchField == SearchField.Pid && vm.PortsView.Cast<InspectorRow>().Any()
            && vm.Page == "Search" && !vm.HasPendingSearch && vm.PortsView.Cast<InspectorRow>().All(r => r.Pid == Environment.ProcessId), "Enter submission or PID field filtering failed.");
        window.SearchFieldPicker.SelectedValue = SearchField.LocalPort;
        window.SearchBox.Text = fixturePort.ToString();
        ClickSearch(window);
        await WaitForAsync(() => vm.PortsView.Cast<InspectorRow>().Any() && vm.PortsView.Cast<InspectorRow>().All(r => r.Port == fixturePort), "Local-port field filtering failed.");
        window.SearchFieldPicker.SelectedValue = SearchField.ProcessName;
        window.SearchBox.Text = "PortPilot";
        window.SearchExactToggle.IsChecked = true;
        ClickSearch(window);
        await WaitForAsync(() => vm.IsExactSearch && !vm.PortsView.Cast<InspectorRow>().Any(), "Exact name matching accepted a partial name.");
        window.SearchExactToggle.IsChecked = false;
        ClickSearch(window);
        await WaitForAsync(() => vm.PortsView.Cast<InspectorRow>().Any(), "Partial-name search no longer works.");
        window.SearchExactToggle.IsChecked = true;
        window.SearchBox.Text = "PORTPILOT.EXE";
        ClickSearch(window);
        await WaitForAsync(() => vm.PortsView.Cast<InspectorRow>().Any(), "Exact name comparison is not case-insensitive.");
        results.Add("Search UI: Enter + search button, scoped PID/port, partial/exact process name: PASS");

        foreach (var theme in new[] { "light", "dark" })
        {
            themes.Apply(theme);
            foreach (var width in new[] { 1200, 900 })
            {
                window.Width = width;
                foreach (var sample in new[] { "gypj 中文进程.exe", @"C:\很长的中文目录 with spaces\development\service\java.exe" })
                {
                    window.SearchBox.Text = sample;
                    window.FocusSearch(); window.UpdateLayout(); await Task.Delay(50);
                    var box = window.SearchBox;
                    var host = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
                    var presenter = FindDescendant<ScrollContentPresenter>(host) ?? throw new InvalidOperationException("Missing TextBox viewport.");
                    var top = presenter.TranslatePoint(new Point(0, 0), box).Y;
                    var caret = box.GetRectFromCharacterIndex(box.Text.Length - 1, true);
                    if (caret.IsEmpty || caret.Height < box.FontSize || caret.Top < top - 0.5 || caret.Bottom > top + presenter.ActualHeight + 0.5)
                        throw new InvalidOperationException($"Search text clipped: {theme}/{width}, caret={caret}, viewport={top}+{presenter.ActualHeight}");
                    results.Add($"Search text viewport {theme}/{width}: line {caret.Height:F1}, viewport {presenter.ActualHeight:F1}, no vertical clipping");
                }
                window.SearchBox.Text = "gypj 中文进程.exe"; window.UpdateLayout();
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
                {
                    var bar = window.SearchBar;
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(bar), null, new Rect(0, 0, bar.ActualWidth, bar.ActualHeight));
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bar.ActualWidth * scale), (int)Math.Ceiling(bar.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, $"search-{theme}-{width}-{scale * 100:0}.png")); encoder.Save(file);
                }
            }
        }
        window.Width = 1200; window.SearchBox.Text = ""; vm.SelectedSearchField = SearchField.All; vm.IsExactSearch = false;
        ClickSearch(window);
        await WaitForAsync(() => !vm.HasPendingSearch && vm.PortsView.Cast<InspectorRow>().Count() == vm.Ports.Count(r => vm.Settings.ShowSystemProcesses || !r.Process.IsSystem), "Submitting empty input did not restore all results.");
        results.Add("Clear and submit: all results restored: PASS");
    }

    private static void ClickSearch(MainWindow window)
        => ((IInvokeProvider)new ButtonAutomationPeer(window.SearchSubmitButton).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task WaitForAsync(Func<bool> ready, string error)
    {
        // Wait for UI automation and dispatcher binding updates on busy hosts.
        for (var i = 0; i < 50; i++) { await Task.Delay(100); if (ready()) return; }
        throw new InvalidOperationException(error);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }
}
