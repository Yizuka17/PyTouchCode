using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PyTouchCode.Core;
public static class ProtocolFrames
{
    public static async Task WriteAsync(Stream stream, object payload, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, ct); await stream.WriteAsync(body, ct); await stream.FlushAsync(ct);
    }
    public static async Task<JsonElement?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new List<byte>(); byte[] one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, ct) == 0) { if (header.Count == 0) return null; throw new EndOfStreamException(); }
            header.Add(one[0]);
            if (header.Count > 8192) throw new InvalidDataException("协议头过大。");
            int n = header.Count;
            if (n >= 4 && header[n-4] == 13 && header[n-3] == 10 && header[n-2] == 13 && header[n-1] == 10) break;
        }
        string text = Encoding.ASCII.GetString(header.ToArray());
        string? length = text.Split("\r\n").FirstOrDefault(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))?.Split(':')[1].Trim();
        if (!int.TryParse(length, out int size) || size < 0 || size > 16 * 1024 * 1024) throw new InvalidDataException("协议长度无效。");
        byte[] body = new byte[size]; await stream.ReadExactlyAsync(body, ct);
        using var doc = JsonDocument.Parse(body); return doc.RootElement.Clone();
    }
}

public sealed class DebugAdapter : IAsyncDisposable
{
    Process? process;
    int sequence;
    Task? reader;
    readonly SemaphoreSlim writeGate = new(1, 1);
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<string, JsonElement>? Event;
    public bool IsRunning => process is { HasExited: false };
    public async Task StartAsync(string python, string file, IEnumerable<int> breakpoints, string cwd, IDictionary<string,string> environment)
    {
        if (IsRunning) throw new InvalidOperationException("调试会话已运行。");
        // The adapter launches Python from the script's working directory.
        // Resolve paths before that directory changes, including imported settings.
        python = Path.GetFullPath(python); file = Path.GetFullPath(file); cwd = Path.GetFullPath(cwd);
        initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        process = new Process { StartInfo = ProcessSession.CreateStartInfo(python, ["-u", "-m", "debugpy.adapter"], cwd, environment) };
        process.Start(); process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Event?.Invoke("adapterError", JsonSerializer.SerializeToElement(new { output = e.Data })); }; process.BeginErrorReadLine();
        reader = ReadLoopAsync();
        await RequestAsync("initialize", new { clientID = "pytouchcode", adapterID = "python", pathFormat = "path", linesStartAt1 = true, columnsStartAt1 = true, supportsVariableType = true, supportsRunInTerminalRequest = false });
        var launch = RequestAsync("launch", new { name = "PyTouchCode", type = "python", request = "launch", program = file, python = new[] { python }, cwd, console = "internalConsole", justMyCode = true, stopOnEntry = !breakpoints.Any(), redirectOutput = true, env = environment });
        if (await Task.WhenAny(initialized.Task, launch).WaitAsync(TimeSpan.FromSeconds(20)) == launch) await launch;
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await SetBreakpointsAsync(file, breakpoints);
        await RequestAsync("setExceptionBreakpoints", new { filters = new[] { "uncaught" } });
        await RequestAsync("configurationDone", new { }); await launch;
    }
    public Task<JsonElement> SetBreakpointsAsync(string file, IEnumerable<int> lines) => RequestAsync("setBreakpoints", new { source = new { path = file }, breakpoints = lines.Select(line => new { line }).ToArray() });
    public async Task<JsonElement> RequestAsync(string command, object arguments)
    {
        var p = process ?? throw new InvalidOperationException("调试会话未启动。");
        int id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = completion;
        try
        {
            await writeGate.WaitAsync();
            try { await ProtocolFrames.WriteAsync(p.StandardInput.BaseStream, new { seq = id, type = "request", command, arguments }); }
            finally { writeGate.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { pending.TryRemove(id, out _); }
    }
    async Task ReadLoopAsync()
    {
        Exception failure = new IOException("调试器已退出。");
        try
        {
            while (await ProtocolFrames.ReadAsync(process!.StandardOutput.BaseStream) is { } message)
            {
                if (message.GetProperty("type").GetString() == "response")
                {
                    if (pending.TryRemove(message.GetProperty("request_seq").GetInt32(), out var completion))
                    {
                        if (message.GetProperty("success").GetBoolean()) completion.TrySetResult(message.TryGetProperty("body", out var body) ? body.Clone() : JsonSerializer.SerializeToElement(new { }));
                        else completion.TrySetException(new IOException(message.TryGetProperty("message", out var error) ? error.GetString() : "调试请求失败。"));
                    }
                }
                else if (message.GetProperty("type").GetString() == "event")
                {
                    string name = message.GetProperty("event").GetString()!;
                    if (name == "initialized") initialized.TrySetResult();
                    Event?.Invoke(name, message.TryGetProperty("body", out var body) ? body.Clone() : JsonSerializer.SerializeToElement(new { }));
                }
            }
        }
        catch (Exception ex) { failure = ex; }
        finally { initialized.TrySetException(failure); foreach (var pair in pending) pair.Value.TrySetException(failure); pending.Clear(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (process is null) return;
        try { if (!process.HasExited) { try { await RequestAsync("disconnect", new { terminateDebuggee = true }).WaitAsync(TimeSpan.FromSeconds(3)); } catch { } if (!process.HasExited) process.Kill(true); } }
        finally { if (reader is not null) await reader; process.Dispose(); process = null; }
    }
}
