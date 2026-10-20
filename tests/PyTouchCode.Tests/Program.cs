using PyTouchCode.Core;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string python = args.FirstOrDefault(x => !x.StartsWith("--")) ?? Path.Combine(root, "runtime", "python", "python.exe");
string bridge = Path.Combine(root, "tools", "python_bridge.py");
string temporary = Path.Combine(root, "artifacts", "tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
int passed = 0, failed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
async Task Test(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + "\n" + e); failed++; }
}

await Test("Workspace create and rename preserve files and reject collisions and traversal", async () =>
{
    string folder = WorkspaceFiles.Create(temporary, "文件夹", true);
    string file = WorkspaceFiles.Create(folder, "数据.py", false);
    await File.WriteAllTextAsync(file, "print('你好')");
    await Throws<IOException>(() => { WorkspaceFiles.Create(folder, "数据.py", false); return Task.CompletedTask; });
    foreach (string invalid in new[] { "../escape.py", "..", "CON.txt", "a/b", "trailing.", "LPT1", "AUX.py", "NUL", "COM²" })
        await Throws<IOException>(() => { WorkspaceFiles.Create(folder, invalid, false); return Task.CompletedTask; });
    string other = WorkspaceFiles.Create(temporary, "existing", true);
    await Throws<IOException>(() => { WorkspaceFiles.Rename(folder, "existing"); return Task.CompletedTask; });
    string renamed = WorkspaceFiles.Rename(folder, "新名称");
    Assert(await File.ReadAllTextAsync(WorkspaceFiles.Rebase(file, folder, renamed)) == "print('你好')", "Rename lost nested content");
    Assert(WorkspaceFiles.Rebase(folder + "-other/file.py", folder, renamed) == folder + "-other/file.py", "Sibling prefix was incorrectly rebased");
    string cased = WorkspaceFiles.Rename(renamed, "NEWNAME");
    cased = WorkspaceFiles.Rename(cased, "newname");
    Assert(Directory.GetDirectories(temporary).Any(p => Path.GetFileName(p) == "newname"), "Case-only rename failed");
});
await Test("Product rename migrates settings and runtimes without deleting legacy data", async () =>
{
    string data = Path.Combine(temporary, "migration"), legacy = Path.Combine(data, "PyTouch"), target = Path.Combine(data, "PyTouchCode");
    string runtime = Path.Combine(legacy, "runtimes", "python.exe"); Directory.CreateDirectory(Path.GetDirectoryName(runtime)!);
    await File.WriteAllTextAsync(runtime, "runtime");
    await new SettingsStore(legacy).SaveAsync(new AppSettings { Interpreter = runtime, EditorFont = "Consolas" });
    await AppDataMigration.MigrateAsync(data);
    var migrated = await new SettingsStore(target).LoadAsync();
    Assert(migrated.Interpreter == Path.Combine(target, "runtimes", "python.exe") && migrated.EditorFont == "Consolas", "Settings or interpreter path was lost");
    Assert(File.Exists(runtime), "Legacy runtime was removed");
    migrated.UiFontSize = 19; await new SettingsStore(target).SaveAsync(migrated); await AppDataMigration.MigrateAsync(data);
    Assert((await new SettingsStore(target).LoadAsync()).UiFontSize == 19, "Migration overwrote existing new settings");
});
await Test("UTF-8 BOM / Unicode / CRLF round trip", async () =>
{
    string file = Path.Combine(temporary, "中文 空格.py");
    var doc = new Document { Text = "# 中文🙂\nprint('你好')\n", Bom = true, NewLine = "\r\n" };
    await DocumentFiles.SaveAsync(doc, file); var loaded = await DocumentFiles.OpenAsync(file);
    Assert(loaded.Text == "# 中文🙂\r\nprint('你好')\r\n" && loaded.Bom && loaded.NewLine == "\r\n", "Round trip changed text or metadata");
});
await Test("UTF-16 and legacy GB18030 round trip", async () =>
{
    foreach (var encoding in new[] { "utf-16", "gb18030" })
    {
        string file = Path.Combine(temporary, encoding + ".py"); var doc = new Document { Text = "中文\n", EncodingName = encoding, Bom = encoding == "utf-16" };
        await DocumentFiles.SaveAsync(doc, file); var loaded = await DocumentFiles.OpenAsync(file, encoding == "gb18030" ? encoding : null);
        Assert(loaded.Text == doc.Text, "Encoding round trip failed");
    }
});
await Test("External edits are preserved", async () =>
{
    string file = Path.Combine(temporary, "external.py"); await File.WriteAllTextAsync(file, "initial");
    var doc = await DocumentFiles.OpenAsync(file); doc.Text = "our edit"; await File.WriteAllTextAsync(file, "external edit");
    await Throws<IOException>(() => DocumentFiles.SaveAsync(doc, file)); Assert(await File.ReadAllTextAsync(file) == "external edit", "External text was clobbered");
});
await Test("Settings persist and validate", async () =>
{
    var store = new SettingsStore(Path.Combine(temporary, "settings"));
    var settings = new AppSettings { Theme = "Dark", Interpreter = python, EditorFontSize = 999, OnlineUpdates = false };
    await store.SaveAsync(settings); var loaded = await store.LoadAsync();
    Assert(loaded.Theme == "Dark" && loaded.Interpreter == python && loaded.EditorFontSize == 40 && !loaded.OnlineUpdates, "Settings lost data");
    await File.WriteAllTextAsync(store.FilePath, "{broken"); await Throws<JsonException>(() => store.LoadAsync());
    Assert(await File.ReadAllTextAsync(store.FilePath) == "{broken", "Corrupt configuration overwritten");
    await Throws<IOException>(() => store.SaveAsync(new AppSettings()));
});
await Test("Scheme catalog and old settings remain compatible", async () =>
{
    Assert(ColorSchemes.BuiltIn.Count >= 9, "Built-in palette list is incomplete");
    foreach (var scheme in ColorSchemes.BuiltIn) scheme.Validate();
    var store = new SettingsStore(Path.Combine(temporary, "old-schema")); Directory.CreateDirectory(store.DirectoryPath);
    await File.WriteAllTextAsync(store.FilePath, "{\"SchemaVersion\":1,\"Theme\":\"Dark\",\"EditorFont\":\"Consolas\"}");
    var loaded = await store.LoadAsync();
    Assert(loaded.EditorDarkScheme == "One Half Dark" && loaded.TerminalLightScheme == "One Half Light" && loaded.EditorFontWeight == 400, "Older preferences lost their defaults");
});
await Test("Independent palettes, font weights and custom colors round trip", async () =>
{
    var custom = ColorSchemes.BuiltIn[0].Clone(); custom.Name = "Test custom"; custom.Background = "#123456";
    var settings = new AppSettings { EditorDarkScheme = custom.Name, TerminalDarkScheme = "Solarized Dark", EditorLightScheme = "Tango Light", TerminalLightScheme = "One Half Light", UiFontSize = 16, UiFontWeight = 600, EditorFontWeight = 700, TerminalFontWeight = 500, EditorLineHeight = 1.5, TerminalLineHeight = 1.25, CustomColorSchemes = [custom] };
    var store = new SettingsStore(Path.Combine(temporary, "appearance")); await store.SaveAsync(settings); var loaded = await store.LoadAsync();
    Assert(ColorSchemes.ForEditor(loaded, true).Background == "#123456" && ColorSchemes.ForTerminal(loaded, true).Name == "Solarized Dark" && loaded.EditorLightScheme == "Tango Light", "Palette choices were coupled or lost");
    Assert(loaded.UiFontWeight == 600 && loaded.EditorFontWeight == 700 && loaded.TerminalFontWeight == 500 && loaded.EditorLineHeight == 1.5 && loaded.TerminalLineHeight == 1.25, "Typography preferences lost");
    custom.Background = "#GG0000"; await Throws<InvalidDataException>(() => store.SaveAsync(settings));
    custom.Background = "#123456"; custom.Name = ColorSchemes.BuiltIn[0].Name; await Throws<InvalidDataException>(() => store.SaveAsync(settings));
});
await Test("Material transparency persists and validates downloaded settings", async () =>
{
    var store = new SettingsStore(Path.Combine(temporary, "material"));
    var settings = new AppSettings { Material = "Acrylic", MaterialTransparency = .73 };
    await store.SaveAsync(settings); var loaded = await store.LoadAsync();
    Assert(loaded.Material == "Acrylic" && loaded.MaterialTransparency == .73, "Material transparency was lost");
    settings.MaterialTransparency = -1; settings.Validate(); Assert(settings.MaterialTransparency == 0, "Negative transparency was not clamped");
    settings.MaterialTransparency = 2; settings.Validate(); Assert(settings.MaterialTransparency == 1, "Transparency above 100% was not clamped");
    settings.MaterialTransparency = double.NaN;
    await Throws<InvalidDataException>(() => { settings.Validate(); return Task.CompletedTask; });
    Assert(JsonSerializer.Deserialize<AppSettings>("{}")!.MaterialTransparency == .5, "Older settings have no material default");
});
await Test("File icon detection is case-insensitive and has folder and unknown fallbacks", () =>
{
    Assert(FileIcons.Resolve("C:/project/MAIN.PY") == "python" && FileIcons.Resolve("data.CSV") == "table" && FileIcons.Resolve("data.cvs") == "table", "Python or tabular file detection failed");
    Assert(FileIcons.Resolve("notes.unknown") == "document" && FileIcons.Resolve("folder.py", true) == "folder" && FileIcons.Resolve("folder.py", true, true) == "folder-open", "File fallback or folder state is wrong");
    return Task.CompletedTask;
});
await Test("PowerShell defaults never load user Profile", () =>
{
    var command = ShellCommand.Resolve(new AppSettings { Shell = "pwsh.exe" });
    Assert(command.Arguments.Contains("-NoProfile") && command.Arguments.Contains("-NoLogo"), "Unsafe shell defaults");
    Assert(!ShellCommand.Resolve(new AppSettings { Shell = "pwsh.exe", EnableShellProfile = true }).Arguments.Contains("-NoProfile"), "Explicit opt-in ignored"); return Task.CompletedTask;
});
await Test("Startup uses bundled Python until the user explicitly selects an interpreter", () =>
{
    string candidate = Path.Combine(temporary, "external-python.exe"); File.Copy(Path.GetFullPath(python), candidate);
    var settings = new AppSettings { Interpreter = candidate };
    PythonEnvironments.SelectStartup(settings, Path.GetFullPath(python));
    Assert(settings.Interpreter == Path.GetFullPath(python) && !settings.InterpreterSelectedByUser, "Startup automatically selected an external interpreter");
    settings.Interpreter = candidate; settings.InterpreterSelectedByUser = true;
    PythonEnvironments.SelectStartup(settings, Path.GetFullPath(python));
    Assert(settings.Interpreter == candidate, "Startup discarded an explicit user selection");
    settings.Interpreter = Path.Combine(temporary, "missing-python.exe");
    PythonEnvironments.SelectStartup(settings, Path.GetFullPath(python));
    Assert(settings.Interpreter == Path.GetFullPath(python) && !settings.InterpreterSelectedByUser, "Missing selected interpreter did not fall back to bundled Python");
    return Task.CompletedTask;
});
await Test("Interpreter probing", async () =>
{
    var environment = await PythonEnvironments.ProbeAsync(python); Assert(environment is not null && environment.Version.StartsWith("3.13."), "Bundled Python not found");
    Assert(await PythonEnvironments.ProbeAsync(Path.Combine(temporary, "missing.exe")) is null, "Nonexistent executable accepted");
});
await Test("Broken interpreter with a missing Python DLL exits silently", async () =>
{
    string folder = Path.Combine(temporary, "broken-python"); Directory.CreateDirectory(folder);
    string exe = Path.Combine(folder, "python.exe"); File.Copy(Path.GetFullPath(python), exe);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    Assert(await PythonEnvironments.ProbeAsync(exe) is null, "Incomplete interpreter was accepted");
    Assert(clock.Elapsed < TimeSpan.FromSeconds(4), "Broken interpreter blocked behind a Windows loader dialog");
});
await Test("Process exit, status polling and repeated input do not access disposed processes", async () =>
{
    await using var session = new ProcessSession();
    for (int n = 0; n < 20; n++)
    {
        var run = session.RunAsync(python, ["-c", "input();print('done')"], temporary);
        var monitor = Task.Run(async () => { while (!run.IsCompleted) { _ = session.IsRunning; await Task.Yield(); } });
        while (!session.IsRunning && !run.IsCompleted) await Task.Yield();
        await session.SendAsync("go");
        await Task.WhenAll(run, monitor); Assert(!session.IsRunning, "Exited process was retained"); session.Stop();
    }
});
await Test("Stopping an active language request cancels cleanly and allows restart", async () =>
{
    string slow = Path.Combine(temporary, "slow_bridge.py");
    await File.WriteAllTextAsync(slow, "import sys,time,json\nfor line in sys.stdin:\n time.sleep(10)\n print('{}',flush=True)\n");
    await using var service = new LanguageService();
    var request = service.RequestAsync(python, slow, new { action = "complete" }, CancellationToken.None);
    await Task.Delay(100); service.Stop();
    await Throws<OperationCanceledException>(async () => await request);
    var result = await service.RequestAsync(python, bridge, new { action = "diagnose", source = "x=1", line = 1, column = 0 }, CancellationToken.None);
    Assert(result.TryGetProperty("diagnostics", out _), "Language worker did not restart");
});
await Test("Serial scanning and execution target settings", async () =>
{
    Assert(SerialDevices.ValidatePort("com17") == "COM17", "COM port was not normalized");
    foreach (string invalid in new[] { "COM0", "COM1 anything", "../port" })
        await Throws<InvalidDataException>(() => { SerialDevices.ValidatePort(invalid); return Task.CompletedTask; });
    var ports = await SerialDevices.DiscoverAsync(python);
    Assert(ports.All(p => p.Port.StartsWith("COM")), "Invalid serial scan results");
    var store = new SettingsStore(Path.Combine(temporary, "device-settings"));
    await store.SaveAsync(new AppSettings { ExecutionTarget = "MicroPython", DevicePort = "COM17" });
    var loaded = await store.LoadAsync(); Assert(loaded.ExecutionTarget == "MicroPython" && loaded.DevicePort == "COM17", "Device target was lost");
});
await Test("Direct process IO including prompts and Unicode paths", async () =>
{
    string file = Path.Combine(temporary, "交互 脚本.py"); await File.WriteAllTextAsync(file, "import sys\nname=input('名字:')\nprint('你好,'+name)\nprint('stderr-marker',file=sys.stderr)\n");
    await using var session = new ProcessSession(); var output = new StringBuilder(); var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    session.Output += (s, _) => { lock (output) output.Append(s); if (s.Contains("名字:")) prompt.TrySetResult(); };
    var run = session.RunAsync(python, ["-u", file], temporary);
    await prompt.Task.WaitAsync(TimeSpan.FromSeconds(10)); await session.SendAsync("测试🙂"); int code = await run;
    Assert(code == 0 && output.ToString().Contains("你好,测试🙂") && output.ToString().Contains("stderr-marker"), "Interactive streams failed");
});
await Test("Tracebacks are not hidden", async () =>
{
    await using var session = new ProcessSession(); var error = new StringBuilder(); session.Output += (s, _) => error.Append(s);
    int code = await session.RunAsync(python, ["-u", "-c", "raise ValueError('actual traceback')"], temporary);
    Assert(code != 0 && error.ToString().Contains("Traceback") && error.ToString().Contains("ValueError: actual traceback"), "Traceback missing");
});
await Test("Cancellation stops process and session can restart", async () =>
{
    await using var session = new ProcessSession(); using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
    var watch = System.Diagnostics.Stopwatch.StartNew(); await session.RunAsync(python, ["-u", "-c", "import time;time.sleep(60)"], temporary, cancellationToken: ct.Token);
    Assert(watch.Elapsed < TimeSpan.FromSeconds(8) && !session.IsRunning, "Process did not stop");
    Assert(await session.RunAsync(python, ["-c", "print('restart')"], temporary) == 0, "Session cannot restart");
});
await Test("Independent PowerShell session accepts commands with no Profile", async () =>
{
    var command = ShellCommand.Resolve(new AppSettings());
    Assert(command.Arguments.Contains("-NoProfile"), "Profile enabled unexpectedly");
    await using var session = new ProcessSession(); var output = new StringBuilder(); var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    session.Output += (s, _) => { lock (output) { output.Append(s); if (output.ToString().Contains("SHELL-SMOKE-你好")) received.TrySetResult(); } };
    using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var run = session.RunAsync(command.Executable, command.Arguments, temporary, cancellationToken: ct.Token);
    await session.SendAsync("Write-Output 'SHELL-SMOKE-你好'");
    await received.Task.WaitAsync(TimeSpan.FromSeconds(10)); await session.SendAsync("exit"); Assert(await run == 0, "Shell exited with error");
});
await Test("Offline Jedi model member completion and environment isolation", async () =>
{
    await using var service = new LanguageService(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    string source = "class Model:\n    def predict(self, values):\n        return values\nmodel = Model()\nmodel.";
    var result = await service.RequestAsync(python, bridge, new { action = "complete", source, line = 5, column = 6, path = Path.Combine(temporary, "model.py") }, ct.Token);
    Assert(result.GetProperty("engine").GetString() == "jedi" && result.GetProperty("completions").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "predict"), "Member completion failed");
});
await Test("Analysis never executes edited code; diagnostics are real", async () =>
{
    await using var service = new LanguageService(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    string marker = Path.Combine(temporary, "must-not-exist.txt");
    string source = $"open({JsonSerializer.Serialize(marker)},'w').write('bad')\nvalue = 1\nval";
    var result = await service.RequestAsync(python, bridge, new { source, line = 3, column = 3 }, ct.Token);
    Assert(!File.Exists(marker), "Edited code was executed");
    result = await service.RequestAsync(python, bridge, new { action = "diagnose", source = "def broken(:\n", line = 1, column = 0 }, ct.Token);
    Assert(result.GetProperty("diagnostics").GetArrayLength() == 1 && result.GetProperty("diagnostics")[0].GetProperty("line").GetInt32() == 1, "Expected real SyntaxError");
});
await Test("Formatting preserves multiline strings", async () =>
{
    await using var service = new LanguageService(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    string source = "text = '''one  \ntwo  \n'''\nvalues=[1,2,3]\n";
    var result = await service.RequestAsync(python, bridge, new { action = "format", source }, ct.Token);
    string formatted = result.GetProperty("formatted").GetString()!;
    Assert(formatted.Contains("one  \ntwo  \n") && formatted.Contains("[1, 2, 3]"), "Formatter changed string semantics or didn't format");
});
await Test("DAP framing uses UTF-8 byte lengths and rejects malformed input", async () =>
{
    using var stream = new MemoryStream(); await ProtocolFrames.WriteAsync(stream, new { text = "中文🙂" }); stream.Position = 0;
    var data = await ProtocolFrames.ReadAsync(stream); Assert(data!.Value.GetProperty("text").GetString() == "中文🙂", "UTF-8 frame corrupted");
    using var malformed = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: -1\r\n\r\n")); await Throws<InvalidDataException>(async () => await ProtocolFrames.ReadAsync(malformed));
});
await Test("debugpy relative interpreter path, breakpoints, locals and stepping", async () =>
{
    string file = Path.Combine(temporary, "debug_test.py"); await File.WriteAllTextAsync(file, "x = 41\ny = x + 1\nprint(y)\n");
    await using var debug = new DebugAdapter(); var stops = Channel.CreateUnbounded<JsonElement>();
    debug.Event += (name, body) => { if (name == "stopped") stops.Writer.TryWrite(body); };
    await debug.StartAsync(python, file, [2], temporary, new Dictionary<string,string> { ["DEBUGPY_LOG_DIR"] = temporary });
    using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var stopped = await stops.Reader.ReadAsync(ct.Token); int thread = stopped.GetProperty("threadId").GetInt32();
    async Task<JsonElement> Locals()
    {
        var stack = await debug.RequestAsync("stackTrace", new { threadId = thread }); int frame = stack.GetProperty("stackFrames")[0].GetProperty("id").GetInt32();
        var scopes = await debug.RequestAsync("scopes", new { frameId = frame }); int reference = scopes.GetProperty("scopes")[0].GetProperty("variablesReference").GetInt32();
        return await debug.RequestAsync("variables", new { variablesReference = reference });
    }
    var variables = await Locals(); Assert(variables.GetProperty("variables").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "x" && x.GetProperty("value").GetString() == "41"), "Local x missing at breakpoint");
    await debug.RequestAsync("next", new { threadId = thread }); await stops.Reader.ReadAsync(ct.Token); variables = await Locals();
    Assert(variables.GetProperty("variables").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "y" && x.GetProperty("value").GetString() == "42"), "Step did not evaluate y");
});
await Test("Remote configuration rejects insecure URL before sending", async () => await Throws<InvalidDataException>(() => RuntimeUpdates.FetchConfigurationAsync("http://example.com/config.json")));
await Test("Tcl/Tk Authenticode rejects tampered installer", async () =>
{
    string original = Path.Combine(root, "artifacts", "downloads", "tcltk-3.13.16.msi");
    Authenticode.VerifyPythonInstaller(original);
    byte[] data = await File.ReadAllBytesAsync(original); data[data.Length / 2] ^= 1;
    string tampered = Path.Combine(temporary, "tampered.msi"); await File.WriteAllBytesAsync(tampered, data);
    await Throws<InvalidDataException>(() => { Authenticode.VerifyPythonInstaller(tampered); return Task.CompletedTask; });
});
if (args.Contains("--online")) await Test("Official runtime update download, validation and Tcl/Tk extraction", async () =>
{
    var updater = new RuntimeUpdates(Path.Combine(temporary, "downloaded-runtime"));
    string version = await updater.CheckPythonAsync() ?? throw new Exception("No stable Python release found");
    string downloaded = await updater.DownloadPythonAsync(version, new Progress<string>(Console.WriteLine));
    var environment = await PythonEnvironments.ProbeAsync(downloaded); Assert(environment?.Version == version, "Downloaded interpreter failed verification");
    await using var session = new ProcessSession(); Assert(await session.RunAsync(downloaded, ["-c", "import tkinter;root=tkinter.Tk();root.withdraw();root.destroy()"], temporary) == 0, "Updated runtime lacks working Tk");
});
Console.WriteLine($"\n{passed} passed, {failed} failed");
Environment.ExitCode = failed == 0 ? 0 : 1;
