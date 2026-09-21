using System.Windows.Data;
using PortPilot.Core.Services;

namespace PortPilot.ViewModels;

public sealed partial class MainViewModel
{
    private void ConfigureLiveFiltering()
    {
        // WPF creates a binding per watched property per row, even for invisible views.
        // Subscribe only to fields that can change membership in the submitted query.
        var properties = new HashSet<string>();
        if (!activeSearch.IsEmpty)
        {
            string[] fields = activeSearch.Field switch
            {
                SearchField.LocalPort => [nameof(InspectorRow.Port)],
                SearchField.RemotePort => [nameof(InspectorRow.RemotePort)],
                SearchField.Pid => [nameof(InspectorRow.Pid)],
                SearchField.ProcessName => [nameof(InspectorRow.Name)],
                SearchField.Path => [nameof(InspectorRow.Path)],
                SearchField.IpAddress => [nameof(InspectorRow.LocalAddress), nameof(InspectorRow.RemoteAddress)],
                _ => [nameof(InspectorRow.Port), nameof(InspectorRow.RemotePort), nameof(InspectorRow.Pid), nameof(InspectorRow.LocalAddress),
                    nameof(InspectorRow.RemoteAddress), nameof(InspectorRow.State), nameof(InspectorRow.Name), nameof(InspectorRow.Path), nameof(InspectorRow.User)]
            };
            properties.UnionWith(fields);
        }
        if (!Settings.ShowSystemProcesses || Filter is "System" or "User Process") properties.Add(nameof(InspectorRow.IsSystem));
        if (Filter is "TCP" or "UDP") properties.Add(nameof(InspectorRow.Protocol));
        if (Filter is "Listening" or "Established") properties.Add(nameof(InspectorRow.State));
        if (Filter == "Localhost") properties.Add(nameof(InspectorRow.LocalAddress));
        foreach (var view in new[] { PortsView, ConnectionsView, ProcessesView }.Cast<ListCollectionView>())
        {
            var needed = new HashSet<string>(properties);
            if (ReferenceEquals(view, ProcessesView) && OnlyNetworkProcesses) needed.Add(nameof(InspectorRow.ConnectionCount));
            if (needed.SetEquals(view.LiveFilteringProperties) && view.IsLiveFiltering == (needed.Count > 0)) continue;
            view.IsLiveFiltering = false;
            view.LiveFilteringProperties.Clear();
            foreach (var property in needed) view.LiveFilteringProperties.Add(property);
            view.IsLiveFiltering = needed.Count > 0;
        }
    }
}
