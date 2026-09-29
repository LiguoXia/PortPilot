using System.Diagnostics;

namespace PortPilot.Core.Mac;

public sealed record CommandOutput(int ExitCode, string Output, string Error);

public static class MacCommand
{
    // Fixed executable paths and ArgumentList avoid shell interpolation of process metadata.
    public static async Task<CommandOutput> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["LC_ALL"] = "C";
        using var process = Process.Start(info) ?? throw new IOException($"无法启动 {executable}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, error).ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException($"{Path.GetFileName(executable)} 超时，请稍后刷新。");
        }
    }
}
