using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using PortPilot.Core.Models;
using PortPilot.Views;

namespace PortPilot.Services;

public sealed class DialogService
{
    public bool Confirm(string title, string message) => new ConfirmDialog(title, message) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
    public void Notify(string title, string message) => MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public bool ConfirmKill(KillPlan plan)
    {
        var message = (plan.Force ? "强制结束可能丢失未保存的数据。" : "将请求进程关闭主窗口；无主窗口的程序需要 Force Kill。")
            + "\n释放端口通过结束整个进程实现，会影响该进程的所有端口与连接。\n\n"
            + string.Join("\n\n", plan.Targets.Select(t => $"{t.Name}   PID {t.Identity.Pid}\n{t.Path}\n当前所有本地端口：{(t.Ports.Length == 0 ? "无" : string.Join(", ", t.Ports))}"));
        return Confirm(plan.Tree ? "结束整个进程树？" : plan.Force ? "强制结束进程？" : "结束进程？", message);
    }
    public void Copy(string text)
    { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { Notify("剪贴板忙", "其他程序正在使用剪贴板，请重试。"); } }
    public void OpenLocation(string path)
    {
        if (!System.IO.File.Exists(path)) { Notify("路径不可用", "文件已不存在，或当前权限不足。"); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
    public void OpenDirectory(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public string? ExportPath()
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json|Text (*.txt)|*.txt", FileName = $"PortPilot-{DateTime.Now:yyyyMMdd-HHmmss}", DefaultExt = ".csv", AddExtension = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public async Task<IReadOnlyList<KillResult>> ElevateOperationAsync(KillPlan plan)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)));
        var operation = Guid.NewGuid().ToString("N");
        var resultFile = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "cache", "operation-" + operation + ".json");
        try
        {
            using var helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--elevated-operation " + payload + " " + operation });
            if (helper != null) await helper.WaitForExitAsync();
            return System.IO.File.Exists(resultFile) ? JsonSerializer.Deserialize<KillResult[]>(await System.IO.File.ReadAllTextAsync(resultFile)) ?? [] : [];
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Notify("已取消", "管理员授权已取消。"); return []; }
        finally { if (System.IO.File.Exists(resultFile)) System.IO.File.Delete(resultFile); }
    }
    public void ElevateReader()
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }); Application.Current.Shutdown(); }
        catch (System.ComponentModel.Win32Exception ex) { Notify("管理员启动未完成", ex.NativeErrorCode == 1223 ? "已取消 UAC 授权。" : ex.Message); }
    }
    public void FocusSearch() { if (Application.Current.MainWindow is MainWindow window) window.FocusSearch(); }
}
