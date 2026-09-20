using Microsoft.Extensions.Logging;
using PortPilot.Core.Models;
using PortPilot.Core.Native;

namespace PortPilot.Core.Services;

public sealed class NetworkService(ProcessService processes, ILogger<NetworkService> logger)
{
    public ScanSnapshot Scan(CancellationToken token)
    {
        var rows = new List<ConnectionSnapshot>(); var warnings = new List<string>();
        foreach (var (tcp, ipv6) in new[] {(true, false), (true, true), (false, false), (false, true)})
        {
            token.ThrowIfCancellationRequested();
            try { rows.AddRange(IpHelperApi.Read(tcp, ipv6)); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidDataException)
            { var message = $"{(tcp ? "TCP" : "UDP")}{(ipv6 ? "6" : "4")} 读取失败：{ex.Message}"; warnings.Add(message); logger.LogWarning(ex, "{Message}", message); }
        }
        return new(rows, processes.Scan(token), DateTimeOffset.Now, warnings);
    }
}
