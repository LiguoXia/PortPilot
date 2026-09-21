using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using PortPilot.Core.Models;
using PortPilot.Core.Services;
using PortPilot.ViewModels;
using PortPilot.Views;

namespace PortPilot.Infrastructure;

internal static class LiveFilteringSmoke
{
    public static async Task VerifyAsync(MainWindow window, List<string> results)
    {
        var vm = window.ViewModel; var auto = vm.Settings.AutoRefresh;
        vm.Settings.AutoRefresh = false; vm.ApplySettingsCommand.Execute(null);
        while (vm.IsBusy) await Task.Delay(50);
        var process = new ProcessSnapshot(987654, 1, "memory-fixture.exe", "fixture", 0, "test", 1, 1024, 1, 1, "Available");
        var connection = new ConnectionSnapshot("TCP", "127.0.0.1", 12345, "127.0.0.1", 1, "ESTABLISHED", process.Pid);
        var row = new InspectorRow("memory-fixture", process, connection, DateTimeOffset.Now);
        vm.Ports.Add(row);
        async Task Flush() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        try
        {
            vm.Filter = "All"; vm.SelectedSearchField = SearchField.RemotePort; vm.SearchText = "9999"; vm.SubmitSearchCommand.Execute(null);
            if (vm.PortsView.Contains(row)) throw new InvalidOperationException("Unexpected remote-port match.");
            connection = connection with { RemotePort = 9999 }; row.Update(process, connection, "", 1, ""); await Flush();
            if (!vm.PortsView.Contains(row)) throw new InvalidOperationException("Remote-port live filtering stopped updating.");
            vm.Filter = "Listening";
            connection = connection with { State = "LISTENING" }; row.Update(process, connection, "", 1, "12345"); await Flush();
            if (!vm.PortsView.Contains(row)) throw new InvalidOperationException("Listening state live filtering stopped updating.");
            vm.SelectedSearchField = SearchField.IpAddress; vm.SearchText = "192.0.2.10"; vm.SubmitSearchCommand.Execute(null);
            connection = connection with { RemoteAddress = "192.0.2.10" }; row.Update(process, connection, "", 1, "12345"); await Flush();
            if (!vm.PortsView.Contains(row)) throw new InvalidOperationException("IP live filtering stopped updating.");
            vm.Filter = "All"; vm.SearchText = ""; vm.SelectedSearchField = SearchField.All; vm.SubmitSearchCommand.Execute(null);
            if (new[] { vm.PortsView, vm.ProcessesView, vm.ConnectionsView }.Cast<ListCollectionView>().Any(v => v.IsLiveFiltering == true || v.LiveFilteringProperties.Count != 0))
                throw new InvalidOperationException("Empty query retained unnecessary per-row filter bindings.");
            vm.PortsView.SortDescriptions.Add(new(nameof(InspectorRow.Cpu), ListSortDirection.Descending));
            row.Update(process with { Cpu = 101 }, connection, "", 1, "12345"); await Flush();
            if (!ReferenceEquals(vm.PortsView.Cast<InspectorRow>().First(), row)) throw new InvalidOperationException("Resource live sorting stopped updating.");
            results.Add("Live filtering: remote port/IP/state updates, unused subscriptions released, CPU live sorting retained: PASS");
        }
        finally
        {
            vm.PortsView.SortDescriptions.Clear(); vm.Ports.Remove(row);
            vm.SelectedSearchField = SearchField.All; vm.SearchText = ""; vm.Filter = "All"; vm.SubmitSearchCommand.Execute(null);
            vm.Settings.AutoRefresh = auto; vm.ApplySettingsCommand.Execute(null);
        }
    }
}
