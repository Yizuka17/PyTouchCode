using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PyTouchCode.Core;
using System.Text;
using Windows.System;
using WinUIEditor;

namespace PyTouchCode.App;
public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; } = new();
    static string? CommandDirectory(string option) { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null; }
    static string? SmokeDirectory => CommandDirectory("--smoke-test");
    static string? PreviewDirectory => CommandDirectory("--preview-directory");
    readonly SettingsStore store = new(SmokeDirectory is { } smoke ? Path.Combine(smoke, "settings") : PreviewDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyTouchCode"));
    AppSettings settings = new();
    readonly ProcessSession runner = new(), shell = new(), repl = new(), device = new(), maintenance = new();
    readonly LanguageService language = new();
    readonly List<EditorTab> documents = [];
    readonly EditorBaseControl[] consoles = new EditorBaseControl[4];
    readonly Queue<(int Channel, string Text)> outputQueue = new();
    readonly DispatcherTimer outputTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    DebugAdapter? debugger;
    bool ready, closing, dialogOpen;
    int debugThread;
    string? bundled;
    string selectedPort = "";
    string WorkingDirectory => Directory.Exists(settings.Workspace) ? settings.Workspace : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string Bridge => Path.Combine(AppContext.BaseDirectory, "tools", "python_bridge.py");
    EditorTab? Active => Tabs.SelectedItem is TabViewItem item ? item.Tag as EditorTab : null;
    public MainWindow()
    {
        App.StartupPhase = "MainWindow XAML"; InitializeComponent(); Title = "PyTouchCode";
        App.StartupPhase = "Window icon";
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PyTouchCode.ico")); }
        catch (Exception e) { throw new InvalidOperationException("设置窗口图标失败。", e); }
        App.StartupPhase = "Menu icons";
        try { AddMenuIcons(); }
        catch (Exception e) { throw new InvalidOperationException("加载菜单图标失败。", e); }
        App.StartupPhase = "Title bar"; ExtendsContentIntoTitleBar = true; SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        for (int i = 0; i < consoles.Length; i++)
        {
            var console = new EditorBaseControl { IsTabStop = true, Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
            consoles[i] = console; OutputHost.Children.Add(console);
            AttachTouch(console);
            AttachTerminal(console, i);
            console.Loaded += (_, _) => { ConfigureEditor(console, true); console.Editor.ReadOnly = false; };
        }
        outputTimer.Tick += (_, _) => FlushOutput();
        ConnectOutput(runner, 0); ConnectOutput(shell, 1); ConnectOutput(repl, 2); ConnectOutput(device, 3); ConnectOutput(maintenance, 0);
        Root.Loaded += async (_, _) => await SafeAsync(InitializeAsync);
        Root.ActualThemeChanged += (_, _) => { if (ready) ApplyAppearance(); };
        AppWindow.Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            await SafeAsync(async () =>
            {
                if (dialogOpen) return;
                foreach (var tab in documents.ToArray()) if (!await ConfirmSaveAsync(tab)) return;
                closing = true; runner.Stop(); shell.Stop(); repl.Stop(); device.Stop(); maintenance.Stop();
                if (debugger is not null) await debugger.DisposeAsync();
                language.Stop(); await store.SaveAsync(settings); Close();
            });
        };
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, "new"); AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, "open");
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, "save"); AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "saveas");
        AddShortcut(VirtualKey.W, VirtualKeyModifiers.Control, "close"); AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, "find");
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, "run"); AddShortcut(VirtualKey.F5, VirtualKeyModifiers.Shift, "stop");
        AddShortcut(VirtualKey.F6, VirtualKeyModifiers.None, "debug"); AddShortcut(VirtualKey.F9, VirtualKeyModifiers.None, "breakpoint");
        AddShortcut(VirtualKey.F10, VirtualKeyModifiers.None, "next"); AddShortcut(VirtualKey.F11, VirtualKeyModifiers.None, "stepIn");
        AddShortcut(VirtualKey.Space, VirtualKeyModifiers.Control, "complete");
        AddShortcut((VirtualKey)191, VirtualKeyModifiers.Control, "comment");
    }
    async Task InitializeAsync()
    {
        App.StartupPhase = "Loaded";
        double scale = Root.XamlRoot.RasterizationScale;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(1280 * scale, display.Width), (int)Math.Min(860 * scale, display.Height)));
        try
        {
            if (SmokeDirectory is null && PreviewDirectory is null) await AppDataMigration.MigrateAsync(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            settings = await store.LoadAsync();
        }
        catch (Exception e) { Notify("配置未加载：" + e.Message + " 原文件已保留，请修复配置后重新启动。"); return; }
        App.StartupPhase = "Appearance"; ApplyAppearance();
        App.StartupPhase = "First document";
        NewDocument("# 欢迎使用 PyTouchCode\n# 新建或打开 Python 文件，然后按 F5 运行。\n\nname = input(\"你的名字：\")\nprint(f\"你好，{name}！\")\n");
        ready = true;
        App.StartupPhase = "Runtime";
        var progress = new Progress<string>(value => ViewModel.InterpreterLabel = value);
        bundled = await new RuntimeUpdates(store.DirectoryPath).EnsureBundledAsync(progress);
        PythonEnvironments.SelectStartup(settings, bundled);
        selectedPort = settings.DevicePort;
        UpdateInterpreter(); RefreshRecent();
        if (Directory.Exists(settings.Workspace)) await RefreshFilesAsync();
        if (PreviewDirectory is not null && FileTree.RootNodes.Count > 0) FileTree.SelectedNode = FileTree.RootNodes[0];
        App.StartupPhase = "Ready";
        if (SmokeDirectory is not null) { await RunSmokeTestsAsync(SmokeDirectory); return; }
        foreach (string file in Environment.GetCommandLineArgs().Skip(1).Where(File.Exists)) await OpenDocumentAsync(file);
        settings.Initialized = true;
        await store.SaveAsync(settings);
    }
    void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, string command)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += async (_, args) => { args.Handled = true; await SafeAsync(() => ExecuteAsync(command)); };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Notify(e.Message, true);
            if (SmokeDirectory is { } directory)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "ui-smoke.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = 0, failed = 1, results = new[] { new { name = "startup: " + App.StartupPhase, passed = false, error = e.ToString() } } }, SettingsStore.JsonOptions));
                closing = true; Close();
            }
        }
    }
    void Notify(string message, bool error = false) { Notice.Message = message; Notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Notice.IsOpen = true; }
    void UpdateInterpreter() => ViewModel.InterpreterLabel = settings.ExecutionTarget == "MicroPython" ? "MicroPython / Pyboard " + settings.DevicePort : File.Exists(settings.Interpreter) ? "Python " + settings.Interpreter : "选择解释器…";
    void ConnectOutput(ProcessSession session, int channel)
    {
        session.Output += (text, error) => QueueOutput(channel, text);
        session.Exited += code => QueueOutput(channel, $"\n[进程退出，代码 {code}]\n");
    }
    void QueueOutput(int channel, string text)
    {
        lock (outputQueue)
        {
            // Bound a producer that writes faster than the UI can paint.
            if (outputQueue.Count > 2048) { outputQueue.Dequeue(); }
            outputQueue.Enqueue((channel, text));
        }
        DispatcherQueue.TryEnqueue(() => { if (!outputTimer.IsEnabled) outputTimer.Start(); });
    }
    void FlushOutput()
    {
        var batches = new StringBuilder[4]; for (int i = 0; i < 4; i++) batches[i] = new();
        lock (outputQueue) { int budget = 256; while (budget-- > 0 && outputQueue.TryDequeue(out var item)) batches[item.Channel].Append(item.Text); if (outputQueue.Count == 0) outputTimer.Stop(); }
        for (int i = 0; i < 4; i++)
        {
            if (batches[i].Length == 0) continue;
            string text = System.Text.RegularExpressions.Regex.Replace(batches[i].ToString(), "\u001b\\[[0-?]*[ -/]*[@-~]", "");
            AppendTerminalOutput(i, text);
        }
    }
    void SelectChannel(int channel) { OutputChannel.SelectedIndex = channel; ShowPanel(true); consoles[channel].Focus(FocusState.Programmatic); }
    void ChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (consoles is null) return;
        for (int i = 0; i < consoles.Length; i++) if (consoles[i] is not null) consoles[i].Visibility = i == OutputChannel.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }
    void ShowPanel(bool show) { PanelHeight.Height = new GridLength(show ? 240 : 0); PanelGripHeight.Height = new GridLength(show ? 6 : 0); OutputPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    void ShowSidebar(bool show) { SidebarWidth.Width = new GridLength(show ? 240 : 0); Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    void SidebarResize(object sender, ManipulationDeltaRoutedEventArgs e) { SidebarWidth.Width = new GridLength(Math.Clamp(SidebarWidth.Width.Value + e.Delta.Translation.X, 160, Math.Max(160, Root.ActualWidth * .45))); e.Handled = true; }
    void PanelResize(object sender, ManipulationDeltaRoutedEventArgs e) { PanelHeight.Height = new GridLength(Math.Clamp(PanelHeight.Height.Value - e.Delta.Translation.Y, 120, Math.Max(120, EditorWorkspace.ActualHeight - 140))); e.Handled = true; }
    async void MenuAction(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string command }) await SafeAsync(() => ExecuteAsync(command)); }
    async Task ExecuteAsync(string command)
    {
        var tab = Active; var ed = tab?.Control.Editor;
        switch (command)
        {
            case "new": NewDocument(); break;
            case "open": await PickOpenAsync(); break;
            case "folder": await PickFolderAsync(); break;
            case "refresh": await RefreshFilesAsync(); break;
            case "save": if (tab is not null) await SaveDocumentAsync(tab); break;
            case "saveas": if (tab is not null) await SaveDocumentAsync(tab, true); break;
            case "saveall": foreach (var t in documents) if (!await SaveDocumentAsync(t)) break; break;
            case "reload": if (tab is not null) await ReloadAsync(tab); break;
            case "close": if (tab is not null) await CloseTabAsync(tab); break;
            case "undo": ed?.Undo(); break; case "redo": ed?.Redo(); break;
            case "cut": ed?.Cut(); break; case "copy": ed?.Copy(); break; case "paste": ed?.Paste(); break; case "selectall": ed?.SelectAll(); break;
            case "find": if (tab is not null) await FindDialogAsync(tab); break;
            case "comment": if (tab is not null) ToggleComment(tab); break;
            case "format": if (tab is not null) await FormatAsync(tab); break;
            case "complete": if (tab is not null) await CompleteAsync(tab, true); break;
            case "sidebar": ShowSidebar(Sidebar.Visibility != Visibility.Visible); break;
            case "panel": ShowPanel(OutputPanel.Visibility != Visibility.Visible); break;
            case "statusbar": StatusBar.Visibility = StatusBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; break;
            case "focus": bool show = Sidebar.Visibility != Visibility.Visible; ShowSidebar(show); ShowPanel(show); break;
            case "fullscreen": AppWindow.SetPresenter(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen); break;
            case "wrap": settings.WordWrap = !settings.WordWrap; ApplyAppearance(); await store.SaveAsync(settings); break;
            case "zoomIn": case "zoomOut": settings.EditorFontSize = Math.Clamp(settings.EditorFontSize + (command == "zoomIn" ? 1 : -1), 9, 40); ApplyAppearance(); await store.SaveAsync(settings); break;
            case "System": case "Dark": case "Light": settings.Theme = command; ApplyAppearance(); await store.SaveAsync(settings); break;
            case "lf": case "crlf": if (tab is not null) { tab.Document.NewLine = command == "lf" ? "\n" : "\r\n"; tab.Document.Dirty = true; UpdateTab(tab); } break;
            case "utf8": case "utf8bom": case "utf16": case "gb18030": if (tab is not null) { tab.Document.EncodingName = command == "utf16" ? "utf-16" : command == "gb18030" ? "gb18030" : "utf-8"; tab.Document.Bom = command is "utf8bom" or "utf16"; tab.Document.Dirty = true; UpdateTab(tab); } break;
            case "run": await RunFileAsync(false); break;
            case "rerun": await runner.StopAsync(); await RunFileAsync(false); break;
            case "debug": await RunFileAsync(true); break;
            case "stop": runner.Stop(); if (OutputChannel.SelectedIndex == 3 && device.IsRunning) await InterruptDeviceAsync(); if (debugger is not null) { var stopped = debugger; debugger = null; await stopped.DisposeAsync(); DebugState.Text = "调试已停止"; } break;
            case "breakpoint": if (tab is not null) await ToggleBreakpointAsync(tab, (int)ed!.LineFromPosition(ed.CurrentPos)); break;
            case "continue": case "next": case "stepIn": case "stepOut": if (debugger is not null && debugThread > 0) await debugger.RequestAsync(command, new { threadId = debugThread }); else Notify("请先启动调试并暂停到断点。"); break;
            case "shell": await StartShellAsync(); break;
            case "repl": await StartReplAsync(); break;
            case "startchannel": if (OutputChannel.SelectedIndex == 1) await StartShellAsync(); else if (OutputChannel.SelectedIndex == 2) await StartReplAsync(); else if (OutputChannel.SelectedIndex == 3) await DeviceDialogAsync(); else await RunFileAsync(false); break;
            case "closeterminal": new[] { runner, shell, repl, device }[Math.Max(0, OutputChannel.SelectedIndex)].Stop(); break;
            case "clear": ClearTerminal(Math.Max(0, OutputChannel.SelectedIndex)); break;
            case "environment": await EnvironmentDialogAsync(); break;
            case "settings": await SettingsDialogAsync(); break;
            case "updates": await UpdatesDialogAsync(); break;
            case "devices": await DeviceDialogAsync(); break;
        }
    }
    async Task RunFileAsync(bool debug)
    {
        if (settings.ExecutionTarget == "MicroPython")
        {
            if (debug) { Notify("MicroPython 暂不支持断点调试。"); return; }
            if (Active is not { } boardTab || !await SaveDocumentAsync(boardTab)) return;
            await MpRemoteAsync(SerialDevices.ValidatePort(settings.DevicePort), "run", boardTab.Document.Path!); return;
        }
        if (runner.IsRunning || debugger?.IsRunning == true) { Notify("请先停止当前运行或调试会话。"); return; }
        if (Active is not { } tab) return;
        if (!File.Exists(settings.Interpreter)) { await EnvironmentDialogAsync(); if (!File.Exists(settings.Interpreter)) return; }
        if (!await SaveDocumentAsync(tab)) return;
        string python = settings.Interpreter, file = tab.Document.Path!;
        SelectChannel(0); QueueOutput(0, $"\n▶ {Path.GetFileName(file)}\n{python}\n");
        if (debug)
        {
            debugger = new DebugAdapter(); debugger.Event += DebugEvent;
            ShowSidebar(true); SidebarPivot.SelectedIndex = 1; DebugState.Text = "正在连接 debugpy…";
            try { await debugger.StartAsync(python, file, BreakpointLines(tab), Path.GetDirectoryName(file)!, PythonProcessEnvironment.Create(python, settings.Environment)); }
            catch { await debugger.DisposeAsync(); debugger = null; throw; }
        }
        else
        {
            ViewModel.SessionStatus = "正在运行 " + Path.GetFileName(file);
            ViewModel.IsRunning = true;
            try { await runner.RunAsync(python, ["-u", file], Path.GetDirectoryName(file)!, PythonProcessEnvironment.Create(python, settings.Environment)); }
            finally { ViewModel.IsRunning = false; ViewModel.SessionStatus = "就绪"; }
        }
    }
    Task StartShellAsync()
    {
        SelectChannel(1); if (shell.IsRunning) return Task.CompletedTask;
        var command = ShellCommand.Resolve(settings);
        _ = SafeAsync(async () => await shell.RunAsync(command.Executable, command.Arguments, WorkingDirectory, PythonProcessEnvironment.Create(settings.Interpreter, settings.Environment)));
        return Task.CompletedTask;
    }
    Task StartReplAsync()
    {
        if (settings.ExecutionTarget == "MicroPython") return device.IsRunning ? Task.CompletedTask : ConnectDeviceAsync(SerialDevices.ValidatePort(settings.DevicePort));
        if (!File.Exists(settings.Interpreter)) { Notify("请先选择 Python 环境。"); return Task.CompletedTask; }
        // Prompts, tracebacks and stdout share one pipe so concurrent readers
        // cannot reorder the prompt around a command's output.
        SelectChannel(2); if (!repl.IsRunning) _ = SafeAsync(async () => await repl.RunAsync(settings.Interpreter, ["-u", "-i", "-c", "import os, sys; os.dup2(sys.stdout.fileno(), sys.stderr.fileno()); sys.stderr = sys.stdout; print(sys.version)"], WorkingDirectory, PythonProcessEnvironment.Create(settings.Interpreter, settings.Environment)));
        return Task.CompletedTask;
    }
}


