using PortPilot.Core.Models;

namespace PortPilot.Core.Mac;

public sealed class MacNetworkService(MacProcessService processes)
{
    public async Task<ScanSnapshot> ScanAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("实时扫描需要 macOS。");
        var output = await MacCommand.RunAsync("/usr/sbin/lsof", ["-n", "-P", "-i", "-F0pftPnT"], token).ConfigureAwait(false);
        if (output.ExitCode is not (0 or 1)) throw new IOException("lsof 读取失败：" + output.Error);
        var warnings = new List<string> { "当前权限可见的快照；受 macOS 隐私与系统保护限制，未发现占用不代表端口可用。" };
        if (!string.IsNullOrWhiteSpace(output.Error)) warnings.Add("lsof 警告：" + output.Error.Trim());
        return new(LsofParser.Parse(output.Output), await processes.ScanAsync(token).ConfigureAwait(false), DateTimeOffset.Now, warnings);
    }
}
