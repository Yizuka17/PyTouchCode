using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace PyTouchCode.Core;
public sealed record PythonEnvironment(string Executable, string Version, string Prefix, string Kind)
{
    public override string ToString() => $"{Kind} Python {Version} — {Executable}";
}
public static class PythonEnvironments
{
    public static void SelectStartup(AppSettings settings, string? bundled)
    {
        if (settings.InterpreterSelectedByUser && File.Exists(settings.Interpreter)) return;
        if (File.Exists(bundled)) { settings.Interpreter = bundled!; settings.InterpreterSelectedByUser = false; }
        else if (!File.Exists(settings.Interpreter)) settings.Interpreter = "";
    }
    public static async Task<PythonEnvironment?> ProbeAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        path = Path.GetFullPath(path);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var p = new Process { StartInfo = ProcessSession.CreateStartInfo(path, ["-c", "import sys,json;print(json.dumps([sys.version.split()[0],sys.prefix]))"], Path.GetDirectoryName(path)!, PythonProcessEnvironment.Create(path)) };
            p.Start();
            using var kill = timeout.Token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } });
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token); var errors = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token); await errors;
            if (p.ExitCode != 0) return null;
            var data = JsonSerializer.Deserialize<string[]>(await output)!;
            if (data is not { Length: 2 } || data.Any(string.IsNullOrWhiteSpace)) return null;
            string prefix = data[1];
            return new(path, data[0], prefix, Directory.Exists(Path.Combine(prefix, "conda-meta")) ? "Conda" : File.Exists(Path.Combine(prefix, "pyvenv.cfg")) ? "venv" : "CPython");
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException) { ct.ThrowIfCancellationRequested(); return null; }
    }
    public static async Task<List<PythonEnvironment>> DiscoverAsync(string? workspace, string? selected, string? bundled, CancellationToken ct = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) candidates.Add(Path.GetFullPath(p)); }
        Add(selected); Add(bundled);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string root in new[] { Path.Combine(home, "miniforge3"), Path.Combine(home, "miniconda3"), Path.Combine(home, "anaconda3"), @"C:\ProgramData\miniforge3" })
        {
            Add(Path.Combine(root, "python.exe"));
            try { if (Directory.Exists(Path.Combine(root, "envs"))) foreach (var env in Directory.EnumerateDirectories(Path.Combine(root, "envs"))) Add(Path.Combine(env, "python.exe")); } catch (UnauthorizedAccessException) { }
        }
        foreach (string line in File.Exists(Path.Combine(home, ".conda", "environments.txt")) ? await File.ReadAllLinesAsync(Path.Combine(home, ".conda", "environments.txt"), ct) : []) Add(Path.Combine(line, "python.exe"));
        foreach (string name in new[] { ".venv", "venv", "env" }) if (!string.IsNullOrEmpty(workspace)) Add(Path.Combine(workspace, name, "Scripts", "python.exe"));
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) Add(Path.Combine(dir.Trim('"'), "python.exe"));
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(@"Software\Python\PythonCore");
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames()) { using var k = key.OpenSubKey(name + @"\InstallPath"); Add(k?.GetValue("ExecutablePath") as string ?? Path.Combine(k?.GetValue("") as string ?? "", "python.exe")); }
            }
        }
        using var limit = new SemaphoreSlim(4);
        var results = await Task.WhenAll(candidates.Take(24).Select(async path =>
        {
            await limit.WaitAsync(ct);
            try { return await ProbeAsync(path, ct); } finally { limit.Release(); }
        }));
        return results.OfType<PythonEnvironment>().ToList();
    }
}

