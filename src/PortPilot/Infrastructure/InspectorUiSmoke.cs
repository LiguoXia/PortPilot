using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PortPilot.ViewModels;
using PortPilot.Views;

namespace PortPilot.Infrastructure;

internal static class InspectorUiSmoke
{
    public static async Task VerifyAsync(MainWindow window, List<string> results)
    {
        var listeners = new List<TcpListener>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        try
        {
            for (var i = 0; i < 12; i++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(); listeners.Add(listener);
            }
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listeners[0].LocalEndpoint);
            using var accepted = await listeners[0].AcceptTcpClientAsync();
            var vm = window.ViewModel;
            await vm.RefreshAsync();
            for (var pass = 0; pass < 3; pass++)
            {
                window.NetworkTab.IsSelected = true;
                window.UpdateLayout(); await Task.Delay(100);
                var rows = vm.DetailConnections.ToArray();
                if (rows.Count(r => r.State == "LISTENING") < 12 || !rows.Any(r => r.Protocol.StartsWith("UDP"))
                    || !rows.Any(r => r.State == "ESTABLISHED"))
                    throw new InvalidOperationException("Network fixtures did not reach the inspector.");
                VerifyRenderedRows(window);
                await vm.RefreshAsync();
                window.UpdateLayout(); await Task.Delay(100);
                VerifyRenderedRows(window);
                window.InspectorTabs.SelectedIndex = pass % 2 == 0 ? 0 : 2;
                window.UpdateLayout();
            }
            var removedPort = ((IPEndPoint)listeners[^1].LocalEndpoint).Port;
            listeners[^1].Stop();
            window.NetworkTab.IsSelected = true;
            await vm.RefreshAsync(); window.UpdateLayout(); await Task.Delay(100);
            if (vm.DetailConnections.Any(r => r.Port == removedPort && r.State == "LISTENING"))
                throw new InvalidOperationException("Closed endpoint remained in Network inspector.");
            VerifyRenderedRows(window);
            results.Add("Network inspector: 12 TCP listeners + UDP + established TCP, three tab switches, refresh and endpoint removal; every protocol/state rendered correctly: PASS");
        }
        finally { foreach (var listener in listeners) listener.Stop(); }
    }

    private static void VerifyRenderedRows(MainWindow window)
    {
        foreach (var row in window.ViewModel.DetailConnections)
        {
            var container = window.NetworkItems.ItemContainerGenerator.ContainerFromItem(row)
                ?? throw new InvalidOperationException("Network row was not rendered.");
            var text = Descendants(container).OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<Run>()).Select(r => r.Text).ToArray();
            if (!text.Contains(row.Protocol) || !text.Contains(row.State))
                throw new InvalidOperationException("Network protocol/state text does not match its current row.");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
