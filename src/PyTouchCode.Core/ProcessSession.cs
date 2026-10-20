using System.Diagnostics;
using System.Text;

namespace PyTouchCode.Core;
public sealed class ProcessSession : IAsyncDisposable
{
    private Process? process;
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsRunning => process is { HasExited: false };
    public event Action<string, bool>? Output;
    public event Action<int>? Exited;
    public static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments, string directory, IDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PYTHONUTF8"] = "1"; info.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var pair in environment ?? new Dictionary<string,string>()) info.Environment[pair.Key] = pair.Value;
        return info;
    }
    public async Task<int> RunAsync(string executable, IEnumerable<string> arguments, string directory, IDictionary<string,string>? environment = null, CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("此会话已有正在运行的进程。");
        try
        {
            process = new Process { StartInfo = CreateStartInfo(executable, arguments, directory, environment) };
            process.Start();
            using var registration = cancellationToken.Register(Stop);
            var stdout = PumpAsync(process.StandardOutput, false);
            var stderr = PumpAsync(process.StandardError, true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            int code = process.ExitCode;
            Exited?.Invoke(code);
            return code;
        }
        finally { process?.Dispose(); process = null; gate.Release(); }
    }
    async Task PumpAsync(StreamReader reader, bool error)
    {
        char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0) Output?.Invoke(new string(buffer, 0, count), error);
    }
    public async Task SendAsync(string value)
    {
        var p = process;
        if (p is not { HasExited: false }) throw new InvalidOperationException("会话未运行。");
        await p.StandardInput.WriteLineAsync(value); await p.StandardInput.FlushAsync();
    }
    public void Stop() { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } }
    public async Task StopAsync() { Stop(); await gate.WaitAsync(); gate.Release(); }
    public async ValueTask DisposeAsync() { Stop(); await gate.WaitAsync(); gate.Release(); gate.Dispose(); }
}

public static class ShellCommand
{
    public static (string Executable, string[] Arguments) Resolve(AppSettings settings)
    {
        string shell = settings.Shell;
        if (string.IsNullOrWhiteSpace(shell))
        {
            shell = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator).Select(x => Path.Combine(x.Trim('"'), "pwsh.exe")).FirstOrDefault(File.Exists)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        }
        bool powershell = Path.GetFileNameWithoutExtension(shell).Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(shell).Equals("powershell", StringComparison.OrdinalIgnoreCase);
        string[] args = powershell ? (settings.EnableShellProfile ? ["-NoLogo", "-NoExit", "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); [Console]::InputEncoding=[Text.UTF8Encoding]::new()"]
            : ["-NoLogo", "-NoProfile", "-NoExit", "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); [Console]::InputEncoding=[Text.UTF8Encoding]::new()"])
            : Path.GetFileNameWithoutExtension(shell).Equals("cmd", StringComparison.OrdinalIgnoreCase) ? ["/d", "/q"] : [];
        return (shell, args.Concat(settings.ShellArguments).ToArray());
    }
}
