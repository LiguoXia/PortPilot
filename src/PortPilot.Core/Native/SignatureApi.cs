using System.Runtime.InteropServices;

namespace PortPilot.Core.Native;

public static class SignatureApi
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo { public uint Size; public IntPtr Path, Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File;
        public uint StateAction; public IntPtr State, Url; public uint Flags, Context; public IntPtr Settings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    public static string Verify(string path)
    {
        var name = Marshal.StringToCoTaskMemUni(path);
        var file = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = file, StateAction = 1, Flags = 0x1000 };
        try
        {
            Marshal.StructureToPtr(new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = name }, file, false);
            var code = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return code switch { 0 => "有效嵌入签名（离线验证，未检查吊销）", unchecked((int)0x800B0100) => "无嵌入签名（未检查目录签名）", _ => $"签名未通过离线验证 (0x{code:X8})" };
        }
        finally
        {
            data.StateAction = 2; _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.FreeHGlobal(file); Marshal.FreeCoTaskMem(name);
        }
    }
}
