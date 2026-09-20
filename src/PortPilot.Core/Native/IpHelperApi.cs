using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using PortPilot.Core.Models;

namespace PortPilot.Core.Native;

public static class IpHelperApi
{
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    private static readonly string[] States = ["UNKNOWN", "CLOSED", "LISTENING", "SYN_SENT", "SYN_RECEIVED", "ESTABLISHED", "FIN_WAIT_1", "FIN_WAIT_2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"];
    public static IReadOnlyList<ConnectionSnapshot> Read(bool tcp, bool ipv6)
    {
        var size = 0;
        uint Fetch(IntPtr p, ref int s) => tcp ? GetExtendedTcpTable(p, ref s, false, ipv6 ? 23 : 2, 5, 0) : GetExtendedUdpTable(p, ref s, false, ipv6 ? 23 : 2, 1, 0);
        var code = Fetch(IntPtr.Zero, ref size);
        if (code != 0 && code != 122) throw new Win32Exception((int)code);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var allocated = Math.Max(size, 4);
            var buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                code = Fetch(buffer, ref size);
                if (code == 122) continue;
                if (code != 0) throw new Win32Exception((int)code);
                var count = Marshal.ReadInt32(buffer);
                var rowSize = ipv6 ? (tcp ? 56 : 28) : (tcp ? 24 : 12);
                if (count < 0 || 4L + (long)count * rowSize > allocated) throw new InvalidDataException("IP Helper table length invalid.");
                var rows = new List<ConnectionSnapshot>(count);
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * rowSize);
                    int D(int o) => Marshal.ReadInt32(row, o);
                    int Port(int o) => (Marshal.ReadByte(row, o) << 8) | Marshal.ReadByte(row, o + 1);
                    string Ip(int o, bool v6, int scope = 0)
                    {
                        var bytes = new byte[v6 ? 16 : 4]; Marshal.Copy(IntPtr.Add(row, o), bytes, 0, bytes.Length);
                        return (v6 ? new IPAddress(bytes, (uint)scope) : new IPAddress(bytes)).ToString();
                    }
                    if (!ipv6 && tcp) rows.Add(new("TCP", Ip(4, false), Port(8), Ip(12, false), Port(16), State(D(0)), D(20)));
                    else if (!ipv6) rows.Add(new("UDP", Ip(0, false), Port(4), "—", 0, "BOUND", D(8)));
                    else if (tcp) rows.Add(new("TCP6", Ip(0, true, D(16)), Port(20), Ip(24, true, D(40)), Port(44), State(D(48)), D(52)));
                    else rows.Add(new("UDP6", Ip(0, true, D(16)), Port(20), "—", 0, "BOUND", D(24)));
                }
                return rows;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new Win32Exception(122, "网络表持续变化，请重试刷新。");
    }
    private static string State(int value) => value >= 0 && value < States.Length ? States[value] : $"UNKNOWN ({value})";
}
