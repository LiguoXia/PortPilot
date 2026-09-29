using System.Globalization;
using PortPilot.Core.Models;

namespace PortPilot.Core.Mac;

public static class LsofParser
{
    // -F0 uses NUL fields, with a newline between process/file sets. Never parse display columns.
    public static IReadOnlyList<ConnectionSnapshot> Parse(string output)
    {
        var rows = new Dictionary<string, ConnectionSnapshot>();
        int pid = 0; string protocol = "", type = "", name = "", state = "";
        void Flush()
        {
            if (pid <= 0 || protocol is not ("TCP" or "UDP") || type is not ("IPv4" or "IPv6")) return;
            var ends = name.Split("->", 2, StringSplitOptions.None);
            if (!Endpoint(ends[0], type, out var local, out var localPort)) return;
            string remote = ""; int remotePort = 0;
            if (ends.Length == 2 && !Endpoint(ends[1], type, out remote, out remotePort)) return;
            var row = new ConnectionSnapshot(protocol + (type == "IPv6" ? "6" : ""), local, localPort,
                remote, remotePort, protocol == "UDP" ? "BOUND" : state == "LISTEN" ? "LISTENING" : string.IsNullOrEmpty(state) ? "UNKNOWN" : state, pid);
            rows[row.Key] = row;
        }
        void Reset() { protocol = type = name = state = ""; }
        foreach (var raw in output.Split('\0'))
        {
            var field = raw.TrimStart('\n', '\r');
            if (field.Length == 0) continue;
            var value = field[1..];
            switch (field[0])
            {
                case 'p': Flush(); Reset(); int.TryParse(value, out pid); break;
                case 'f': Flush(); Reset(); break;
                case 'P': protocol = value; break;
                case 't': type = value; break;
                case 'n': name = value; break;
                case 'T' when value.StartsWith("ST=", StringComparison.Ordinal): state = value[3..]; break;
            }
        }
        Flush(); return rows.Values.ToArray();
    }
    private static bool Endpoint(string text, string type, out string address, out int port)
    {
        address = ""; port = 0;
        var colon = text.LastIndexOf(':');
        if (colon < 1) return false;
        address = text[..colon].Trim('[', ']');
        if (address == "*") address = type == "IPv6" ? "::" : "0.0.0.0";
        var value = text[(colon + 1)..];
        return value == "*" || int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 0 and <= 65535;
    }
}
