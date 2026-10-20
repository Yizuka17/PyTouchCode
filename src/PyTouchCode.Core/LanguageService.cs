using System.Diagnostics;
using System.Text.Json;

namespace PyTouchCode.Core;
public sealed class LanguageService : IAsyncDisposable
{
    sealed class Worker(Process process) : IDisposable
    {
        public Process Process { get; } = process;
        public CancellationTokenSource Lifetime { get; } = new();
        public bool Stopped;
        public int Users;
        public void Dispose() { Process.Dispose(); Lifetime.Dispose(); }
    }
    Worker? worker;
    string? interpreter, environmentKey;
    bool disposed;
    readonly object state = new();
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<JsonElement> RequestAsync(string python, string bridge, object request, CancellationToken ct, IDictionary<string,string>? environment = null)
    {
        await gate.WaitAsync(ct); Worker? current = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            string key = JsonSerializer.Serialize(environment ?? new Dictionary<string,string>());
            lock (state)
            {
                if (worker is null || worker.Process.HasExited || interpreter != python || environmentKey != key)
                {
                    Stop(); interpreter = python; environmentKey = key;
                    var process = new Process { StartInfo = ProcessSession.CreateStartInfo(python, ["-u", bridge], Path.GetDirectoryName(bridge)!, PythonProcessEnvironment.Create(python, environment)) };
                    try { process.Start(); process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine(); }
                    catch { process.Dispose(); throw; }
                    worker = new Worker(process);
                }
                current = worker; current.Users++;
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, current.Lifetime.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await current.Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), lifetime.Token);
                await current.Process.StandardInput.FlushAsync(lifetime.Token);
                var line = await current.Process.StandardOutput.ReadLineAsync(timeout.Token) ?? throw new IOException("语言服务意外退出。");
                lifetime.Token.ThrowIfCancellationRequested();
                using var json = JsonDocument.Parse(line); return json.RootElement.Clone();
            }
            catch
            {
                bool stopped = current.Lifetime.IsCancellationRequested;
                lock (state) { if (ReferenceEquals(worker, current)) Stop(); }
                if (stopped) throw new OperationCanceledException("语言服务已停止。", lifetime.Token);
                throw;
            }
        }
        finally
        {
            lock (state) { if (current is not null && --current.Users == 0 && current.Stopped) current.Dispose(); }
            gate.Release();
        }
    }
    public void Stop()
    {
        lock (state)
        {
            var stopped = worker; worker = null;
            if (stopped is null) return;
            stopped.Stopped = true; stopped.Lifetime.Cancel();
            try { if (!stopped.Process.HasExited) stopped.Process.Kill(true); } catch (InvalidOperationException) { }
            if (stopped.Users == 0) stopped.Dispose();
        }
    }
    public async ValueTask DisposeAsync() { Stop(); await gate.WaitAsync(); disposed = true; gate.Release(); }
}
