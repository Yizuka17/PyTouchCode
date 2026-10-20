using System.Diagnostics;
using System.Text;

namespace PyTouchCode.Core;
public sealed class ProcessSession : IAsyncDisposable
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetErrorMode();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint SetErrorMode(uint mode);
    static ProcessSession()
    {
        // Child processes inherit this mode. Broken interpreter candidates must
        // exit with an error rather than block discovery behind a loader dialog.
        if (OperatingSystem.IsWindows()) SetErrorMode(GetErrorMode() | 0x8003);
    }
    private Process? process;
    private readonly object state = new();
    private bool disposed;
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsRunning { get { lock (state) return process is { HasExited: false }; } }
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
        Process? current = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            current = new Process { StartInfo = CreateStartInfo(executable, arguments, directory, environment) };
            current.Start();
            lock (state) process = current;
            using var registration = cancellationToken.Register(Stop);
            var stdout = PumpAsync(current.StandardOutput, false);
            var stderr = PumpAsync(current.StandardError, true);
            await current.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            int code = current.ExitCode;
            Exited?.Invoke(code);
            return code;
        }
        finally { lock (state) { process = null; current?.Dispose(); } gate.Release(); }
    }
    async Task PumpAsync(StreamReader reader, bool error)
    {
        char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0) Output?.Invoke(new string(buffer, 0, count), error);
    }
    public async Task SendAsync(string value)
    {
        StreamWriter input;
        lock (state)
        {
            if (process is not { HasExited: false } current) throw new InvalidOperationException("会话未运行。");
            input = current.StandardInput;
        }
        try { await input.WriteLineAsync(value); await input.FlushAsync(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Python may consume the final input and exit before Flush completes.
            if (IsRunning) throw new IOException("无法向进程发送输入。", e);
        }
    }
    public void Stop()
    {
        lock (state) { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } }
    }
    public async Task StopAsync() { Stop(); await gate.WaitAsync(); gate.Release(); }
    public async ValueTask DisposeAsync() { Stop(); await gate.WaitAsync(); disposed = true; gate.Release(); }

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
