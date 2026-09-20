using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PortPilot.Core.Native;

public static class ProcessNativeApi
{
    public const uint Query = 0x1000, Terminate = 1, ReadMemory = 0x10, QueryInformation = 0x400;
    [DllImport("kernel32.dll", SetLastError = true)] public static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessHandleCount(SafeProcessHandle handle, out uint count);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32GetProcessMemoryInfo(SafeProcessHandle handle, ref MemoryCounters counters, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryCounters
    {
        public uint Size, PageFaults;
        public nuint PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, PrivateUsage;
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool IsProcessCritical(SafeProcessHandle handle, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(SafeProcessHandle handle, out ushort processMachine, out ushort nativeMachine);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, out int value, int length, out int returned);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle process, int infoClass, IntPtr info, int length, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(SafeSnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(SafeSnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    private sealed class SafeSnapshotHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    { protected override bool ReleaseHandle() => CloseHandle(handle); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, ParentPid; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    public static Dictionary<int, (int Parent, string Name, int Threads)> Parents()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = "" };
        var result = new Dictionary<int, (int, string, int)>();
        if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        do { result[(int)entry.Pid] = ((int)entry.ParentPid, entry.Name, (int)entry.Threads); } while (Process32NextW(snapshot, ref entry));
        var code = Marshal.GetLastWin32Error();
        if (code != 18) throw new Win32Exception(code);
        return result;
    }
    public static long StartTicks(SafeProcessHandle handle)
    {
        if (!GetProcessTimes(handle, out var start, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return DateTime.FromFileTimeUtc(start).Ticks;
    }
    public static void EnsureRunning(SafeProcessHandle handle)
    {
        if (!GetProcessTimes(handle, out _, out var exit, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (exit != 0) throw new InvalidOperationException("Process exited / Process no longer exists");
    }
    public static (TimeSpan Cpu, long Memory, int Handles) Resources(SafeProcessHandle handle)
    {
        if (!GetProcessTimes(handle, out _, out _, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        if (!K32GetProcessMemoryInfo(handle, ref memory, memory.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetProcessHandleCount(handle, out var handles)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (TimeSpan.FromTicks(kernel + user), checked((long)memory.WorkingSet), checked((int)handles));
    }
    public static string ImagePath(SafeProcessHandle handle)
    {
        var size = 32768; var path = new StringBuilder(size);
        if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }
    public static (string User, string Elevated) Token(SafeProcessHandle handle)
    {
        if (!OpenProcessToken(handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            if (!GetTokenInformation(token, 20, out var elevated, 4, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return (identity.Name, elevated != 0 ? "Administrator" : "Standard");
        }
    }
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static string Architecture(SafeProcessHandle handle)
    {
        if (!IsWow64Process2(handle, out var process, out var native)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (process == 0 ? native : process) switch { 0x8664 => "x64", 0x14c => "x86", 0xaa64 => "ARM64", _ => "Unknown" };
    }
    public static string CommandLine(SafeProcessHandle handle)
    {
        _ = NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out var needed);
        if (needed <= 0 || needed > 1024 * 1024) throw new InvalidOperationException("Command line unavailable");
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            var status = NtQueryInformationProcess(handle, 60, buffer, needed, out _);
            if (status < 0) throw new InvalidOperationException($"Command line unavailable (NTSTATUS 0x{status:X8})");
            var length = (ushort)Marshal.ReadInt16(buffer);
            var ptr = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
            if (ptr.ToInt64() < buffer.ToInt64() || ptr.ToInt64() + length > buffer.ToInt64() + needed) throw new InvalidDataException("Invalid command-line buffer");
            return Marshal.PtrToStringUni(ptr, length / 2) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    // PEB layout is an implementation detail; failures are shown as unavailable, never fatal.
    public static (string Directory, string[] EnvironmentNames) Parameters(int pid)
    {
        using var handle = OpenProcess(QueryInformation | ReadMemory, false, pid);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var x86 = Architecture(handle) == "x86";
        var info = Marshal.AllocHGlobal(48);
        try
        {
            var status = NtQueryInformationProcess(handle, x86 ? 26 : 0, info, 48, out _);
            if (status < 0) throw new InvalidOperationException($"PEB unavailable (0x{status:X8})");
            var peb = Marshal.ReadIntPtr(info, x86 ? 0 : 8).ToInt64();
            byte[] Read(long address, int count)
            {
                var bytes = new byte[count];
                if (!ReadProcessMemory(handle, new IntPtr(address), bytes, (nuint)count, out var read) || read != (nuint)count) throw new Win32Exception(Marshal.GetLastWin32Error());
                return bytes;
            }
            long Pointer(long address) { var b = Read(address, x86 ? 4 : 8); return x86 ? BitConverter.ToUInt32(b) : BitConverter.ToInt64(b); }
            var parameters = Pointer(peb + (x86 ? 0x10 : 0x20));
            var current = parameters + (x86 ? 0x24 : 0x38);
            var length = BitConverter.ToUInt16(Read(current, 2));
            var directory = Encoding.Unicode.GetString(Read(Pointer(current + (x86 ? 4 : 8)), length));
            // Only names are exposed. Never persist or log environment values.
            try
            {
                var env = Pointer(parameters + (x86 ? 0x48 : 0x80));
                var chars = new List<byte>();
                for (var i = 0; env != 0 && i < 65536; i += 2)
                {
                    var pair = Read(env + i, 2); chars.AddRange(pair);
                    if (chars.Count >= 4 && chars[^1] == 0 && chars[^2] == 0 && chars[^3] == 0 && chars[^4] == 0) break;
                }
                var names = Encoding.Unicode.GetString(chars.ToArray()).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !s.StartsWith('=')).Select(s => s.Split('=', 2)[0]).Distinct().Order().ToArray();
                return (directory, names);
            }
            catch (Win32Exception ex)
            {
                // A process can replace its environment block while it is being read.
                // Preserve the independently read directory and surface the failure explicitly.
                return (directory, [$"Unavailable: environment changed / access denied (Win32 {ex.NativeErrorCode})"]);
            }
        }
        finally { Marshal.FreeHGlobal(info); }
    }
}
