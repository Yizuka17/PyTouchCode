using System.Diagnostics;
using System.Text.Json;

namespace PyTouch.Core;
public sealed class LanguageService : IAsyncDisposable
{
    Process? worker;
    string? interpreter;
    string? environmentKey;
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<JsonElement> RequestAsync(string python, string bridge, object request, CancellationToken ct, IDictionary<string,string>? environment = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            string key = JsonSerializer.Serialize(environment ?? new Dictionary<string,string>());
            if (worker is not { HasExited: false } || interpreter != python || environmentKey != key)
            {
                Stop(); interpreter = python; environmentKey = key;
                worker = new Process { StartInfo = ProcessSession.CreateStartInfo(python, ["-u", bridge], Path.GetDirectoryName(bridge)!, PythonProcessEnvironment.Create(python, environment)) };
                worker.Start();
                // Drain stderr even when a native third-party library writes startup messages.
                worker.ErrorDataReceived += (_, _) => { }; worker.BeginErrorReadLine();
            }
            await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await worker.StandardInput.FlushAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var line = await worker.StandardOutput.ReadLineAsync(timeout.Token) ?? throw new IOException("语言服务意外退出。");
                using var json = JsonDocument.Parse(line); return json.RootElement.Clone();
            }
            catch { Stop(); throw; } // Never leave a canceled response in the next request's stream.
        }
        finally { gate.Release(); }
    }
    public void Stop() { try { if (worker is { HasExited: false }) worker.Kill(true); } catch (InvalidOperationException) { } worker?.Dispose(); worker = null; }
    public async ValueTask DisposeAsync() { Stop(); await gate.WaitAsync(); gate.Release(); gate.Dispose(); }
}
