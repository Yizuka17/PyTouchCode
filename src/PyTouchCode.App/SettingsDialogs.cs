using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PyTouchCode.Core;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    static ComboBox Choice(string header, IEnumerable<string> values, string selected)
    { var control = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch }; foreach (var value in values) control.Items.Add(value); control.SelectedItem = selected; return control; }
    static TextBox Field(string header, string value) => new() { Header = header, Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
    static NumberBox Number(string header, double value, double min, double max) => new() { Header = header, Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };
    static StackPanel Form() => new() { Spacing = 14, MinWidth = 380, Margin = new Thickness(0, 0, 12, 12) };
    static ScrollViewer Scroll(UIElement content) => new() { Content = content, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    static void Add(StackPanel panel, params UIElement[] elements) { foreach (var item in elements) panel.Children.Add(item); }
    async Task SettingsDialogAsync()
    {
        var appearance = Form(); var editor = Form(); var terminal = Form();
        var theme = Choice("应用主题", ["System", "Dark", "Light"], settings.Theme);
        var material = Choice("窗口材质", ["Mica", "MicaAlt", "Acrylic", "Solid"], settings.Material);
        var accent = Field("自定义强调色", settings.Accent); var systemAccent = Check("跟随 Windows 强调色", settings.UseSystemAccent);
        var uiFont = FontChoice("界面字体", settings.UiFont); var editorFont = FontChoice("编辑器字体", settings.EditorFont); var terminalFont = FontChoice("终端字体", settings.TerminalFont);
        var uiSize = Number("界面字号", settings.UiFontSize, 10, 24);
        var uiWeight = WeightChoice("界面字重", settings.UiFontWeight); var editorWeight = WeightChoice("编辑器字重", settings.EditorFontWeight); var terminalWeight = WeightChoice("终端字重", settings.TerminalFontWeight);
        var editorSize = Number("代码字号", settings.EditorFontSize, 9, 40); var terminalSize = Number("终端字号", settings.TerminalFontSize, 9, 32);
        var editorHeight = Number("编辑器行高倍数", settings.EditorLineHeight, 1, 2); var terminalHeight = Number("终端行高倍数", settings.TerminalLineHeight, 1, 2);
        editorHeight.SmallChange = terminalHeight.SmallChange = .1;
        var schemes = new SchemeSettings(settings);
        var indent = Number("缩进宽度", settings.IndentSize, 1, 8); var tabs = Check("使用 Tab 缩进", settings.UseTabs); var wrap = Check("代码自动换行", settings.WordWrap);
        var complete = Check("离线补全与语法诊断", settings.Completion); var ghost = Check("灰色行内预测（Tab 接受）", settings.GhostText);
        var shellPath = Field("Shell 路径", settings.Shell); var profile = Check("允许加载 PowerShell Profile", settings.EnableShellProfile);
        var shellArgs = Field("附加启动参数（JSON 数组）", JsonSerializer.Serialize(settings.ShellArguments));
        var env = Field("进程环境变量（JSON 对象）", JsonSerializer.Serialize(settings.Environment)); env.AcceptsReturn = true; env.MinHeight = 80;
        var editorPalette = ColorSchemes.ForEditor(settings, Root.ActualTheme == ElementTheme.Dark); var terminalPalette = ColorSchemes.ForTerminal(settings, Root.ActualTheme == ElementTheme.Dark);
        Add(appearance, theme, material, systemAccent, accent, uiFont, uiSize, uiWeight, FontPreview(uiFont, uiSize, uiWeight, editorPalette));
        Add(editor, editorFont, editorSize, editorWeight, editorHeight, FontPreview(editorFont, editorSize, editorWeight, editorPalette, editorHeight), indent, tabs, wrap, complete, ghost);
        Add(terminal, terminalFont, terminalSize, terminalWeight, terminalHeight, FontPreview(terminalFont, terminalSize, terminalWeight, terminalPalette, terminalHeight), shellPath, profile, shellArgs, env);
        var navigation = NavigationTabs(("外观", Scroll(appearance)), ("配色方案", Scroll(schemes.View)), ("编辑器", Scroll(editor)), ("终端", Scroll(terminal)));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; var panel = Form(); Add(panel, navigation, error);
        var dialog = new ContentDialog { Title = "设置", Content = panel, PrimaryButtonText = "保存设置", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMaxWidth"] = 680d;
        AppSettings? accepted = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                ParseColor(accent.Text);
                if (new[] { uiSize.Value, editorSize.Value, terminalSize.Value, editorHeight.Value, terminalHeight.Value, indent.Value }.Any(n => !double.IsFinite(n))) throw new InvalidDataException("请填写有效数字。");
                var arguments = JsonSerializer.Deserialize<string[]>(shellArgs.Text) ?? [];
                var variables = JsonSerializer.Deserialize<Dictionary<string,string>>(env.Text) ?? [];
                if (arguments.Any(x => x is null) || variables.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Contains('=') || x.Value is null)) throw new InvalidDataException("环境变量或启动参数无效。");
                var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings), SettingsStore.JsonOptions)!;
                candidate.Theme = (string)theme.SelectedItem; candidate.Material = (string)material.SelectedItem; candidate.UseSystemAccent = systemAccent.IsChecked == true; candidate.Accent = accent.Text;
                candidate.UiFont = FontName(uiFont); candidate.EditorFont = FontName(editorFont); candidate.TerminalFont = FontName(terminalFont);
                candidate.UiFontSize = uiSize.Value; candidate.UiFontWeight = FontWeightValue(uiWeight);
                candidate.EditorFontSize = editorSize.Value; candidate.TerminalFontSize = terminalSize.Value; candidate.IndentSize = (int)indent.Value;
                candidate.EditorFontWeight = FontWeightValue(editorWeight); candidate.TerminalFontWeight = FontWeightValue(terminalWeight);
                candidate.EditorLineHeight = editorHeight.Value; candidate.TerminalLineHeight = terminalHeight.Value;
                candidate.UseTabs = tabs.IsChecked == true; candidate.WordWrap = wrap.IsChecked == true; candidate.Completion = complete.IsChecked == true; candidate.GhostText = ghost.IsChecked == true;
                candidate.Shell = shellPath.Text.Trim(); candidate.EnableShellProfile = profile.IsChecked == true; candidate.ShellArguments = arguments; candidate.Environment = variables;
                schemes.Apply(candidate); candidate.Validate(); accepted = candidate;
            }
            catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary && accepted is not null) { settings = accepted; ApplyAppearance(); await store.SaveAsync(settings); }
    }
    async Task EnvironmentDialogAsync(bool firstRun = false)
    {
        using var lifetime = new CancellationTokenSource();
        var target = Choice("解释器", ["Python", "MicroPython / Pyboard"], settings.ExecutionTarget == "MicroPython" ? "MicroPython / Pyboard" : "Python");
        var panel = Form(); var pythonPanel = Form(); var boardPanel = Form();
        var path = Field("python.exe 路径", settings.Interpreter);
        var environments = new ComboBox { Header = "Python 环境", HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var scan = new Button { Content = "扫描 Python / Conda / venv" }; var browse = new Button { Content = "浏览 python.exe…" };
        var ports = new ComboBox { Header = "USB 串口", IsEditable = true, Text = settings.DevicePort, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scanPorts = new Button { Content = "扫描串口" };
        Add(pythonPanel, environments, path, browse, scan); Add(boardPanel, ports, scanPorts); Add(panel, target, pythonPanel, boardPanel, status);
        bool IsBoard() => target.SelectedIndex == 1;
        var dialog = new ContentDialog { Title = firstRun ? "开始使用 PyTouchCode" : "解释器", Content = Scroll(panel), PrimaryButtonText = "使用此环境", CloseButtonText = firstRun ? "稍后配置" : "取消" };
        environments.SelectionChanged += (_, _) => { if (environments.SelectedItem is PythonEnvironment value) path.Text = value.Executable; };
        async Task Scan()
        {
            bool board = IsBoard(); scan.IsEnabled = scanPorts.IsEnabled = target.IsEnabled = false; status.Text = "正在扫描…";
            try
            {
                if (board)
                {
                    var found = await SerialDevices.DiscoverAsync(DevicePython, lifetime.Token);
                    string previous = (ports.SelectedItem as SerialDevice)?.Port ?? ports.Text;
                    ports.Items.Clear(); foreach (var value in found) ports.Items.Add(value);
                    ports.SelectedItem = found.FirstOrDefault(x => string.Equals(x.Port, previous, StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault();
                    if (ports.SelectedItem is null) ports.Text = previous;
                    status.Text = found.Count == 0 ? "未发现串口，可连接设备后重试。" : $"发现 {found.Count} 个串口";
                }
                else
                {
                    var found = await PythonEnvironments.DiscoverAsync(settings.Workspace, path.Text, bundled, lifetime.Token);
                    string previous = path.Text; environments.Items.Clear(); foreach (var value in found) environments.Items.Add(value);
                    environments.SelectedItem = found.FirstOrDefault(x => string.Equals(x.Executable, previous, StringComparison.OrdinalIgnoreCase));
                    if (environments.SelectedItem is null && string.IsNullOrWhiteSpace(previous)) environments.SelectedItem = found.FirstOrDefault();
                    status.Text = found.Count == 0 ? "未找到可用环境，可手动指定 python.exe。" : $"发现 {found.Count} 个可用环境";
                }
            }
            catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "扫描超时，请重试。"; }
            catch (Exception e) { status.Text = e.Message; }
            finally { scan.IsEnabled = scanPorts.IsEnabled = target.IsEnabled = true; }
        }
        void UpdateTarget()
        {
            pythonPanel.Visibility = IsBoard() ? Visibility.Collapsed : Visibility.Visible;
            boardPanel.Visibility = IsBoard() ? Visibility.Visible : Visibility.Collapsed;
            dialog.PrimaryButtonText = IsBoard() ? "使用此设备" : "使用此环境"; status.Text = "";
        }
        target.SelectionChanged += (_, _) => UpdateTarget();
        scan.Click += async (_, _) => await Scan(); scanPorts.Click += async (_, _) => await Scan();
        browse.Click += async (_, _) => await SafeAsync(async () =>
        {
            var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync(); if (file is not null) path.Text = file.Path;
        });
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (IsBoard())
                {
                    settings.DevicePort = SerialDevices.ValidatePort((ports.SelectedItem as SerialDevice)?.Port ?? ports.Text);
                    settings.ExecutionTarget = "MicroPython"; selectedPort = settings.DevicePort;
                }
                else
                {
                    var selected = await PythonEnvironments.ProbeAsync(path.Text.Trim(), lifetime.Token);
                    if (selected is null) { args.Cancel = true; status.Text = "解释器不可用，请选择其他环境。"; return; }
                    settings.Interpreter = selected.Executable; settings.InterpreterSelectedByUser = true; settings.ExecutionTarget = "Python";
                }
                settings.Initialized = true; language.Stop();
            }
            catch (Exception e) { args.Cancel = true; status.Text = e.Message; }
            finally { deferral.Complete(); }
        };
        if (File.Exists(bundled))
        {
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RuntimeUpdates.BundledDirectory, "pytouchcode-runtime.json")));
            var value = new PythonEnvironment(bundled!, info.RootElement.GetProperty("version").GetString()!, Path.GetDirectoryName(bundled!)!, "内置");
            environments.Items.Add(value);
            if (string.Equals(path.Text, bundled, StringComparison.OrdinalIgnoreCase)) environments.SelectedItem = value;
        }
        UpdateTarget();
        dialog.Closed += (_, _) => lifetime.Cancel();
        await ShowDialogAsync(dialog); settings.Initialized = true; UpdateInterpreter(); await store.SaveAsync(settings);
    }
    async Task UpdatesDialogAsync()
    {
        var panel = Form(); var enabled = Check("允许手动联网获取环境、工具和配置更新", settings.OnlineUpdates);
        var check = new Button { Content = "检查 Python 与工具更新" }; var runtime = new Button { Content = "下载并切换到新版内置 Python", IsEnabled = false };
        var tools = new Button { Content = "为当前解释器安装 / 更新开发工具" }; var ml = new Button { Content = "为当前解释器安装机器学习学习包" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var source = Field("可选的远程配置 URL（HTTPS JSON）", settings.ConfigurationUrl);
        var fetch = new Button { Content = "获取配置预览" }; var apply = new Button { Content = "应用预览中的外观与编辑器配置", IsEnabled = false };
        string? latest = null; AppSettings? fetched = null; bool downloading = false;
        var lifetime = new CancellationTokenSource();
        bool CanNetwork() { if (enabled.IsChecked == true) return true; status.Text = "请先允许联网。"; return false; }
        async Task Work(Func<Task> task)
        {
            if (downloading || !CanNetwork()) return; downloading = true;
            check.IsEnabled = runtime.IsEnabled = tools.IsEnabled = ml.IsEnabled = fetch.IsEnabled = false;
            try { await task(); }
            catch (Exception e) { status.Text = "更新未完成：" + e.Message; }
            finally { downloading = false; check.IsEnabled = tools.IsEnabled = ml.IsEnabled = fetch.IsEnabled = true; runtime.IsEnabled = latest is not null; }
        }
        async Task Install(string python, string[] packages)
        {
            if (runner.IsRunning || repl.IsRunning || debugger?.IsRunning == true) throw new InvalidOperationException("请先停止 Python 运行、REPL 和调试会话。");
            language.Stop(); status.Text = "正在安装，详细进度见运行输出…"; SelectChannel(0);
            if (await maintenance.RunAsync(python, ["-m", "pip", "--version"], WorkingDirectory, settings.Environment, lifetime.Token) != 0 && await maintenance.RunAsync(python, ["-m", "ensurepip", "--upgrade"], WorkingDirectory, settings.Environment, lifetime.Token) != 0) throw new IOException("pip 初始化失败。");
            if (await maintenance.RunAsync(python, new[] { "-m", "pip", "install", "--upgrade", "--disable-pip-version-check", "--no-warn-script-location" }.Concat(packages), WorkingDirectory, settings.Environment, lifetime.Token) != 0) throw new IOException("pip 安装失败，请检查运行输出。");
            status.Text = "安装完成。";
        }
        check.Click += async (_, _) => await Work(async () =>
        {
            status.Text = "正在获取官方版本信息…";
            latest = await new RuntimeUpdates(store.DirectoryPath).CheckPythonAsync(lifetime.Token);
            using var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
            var list = new List<string> { "Python 3.13 稳定版：" + latest };
            foreach (string name in RuntimeUpdates.ToolPackages)
            { using var json = JsonDocument.Parse(await client.GetStringAsync($"https://pypi.org/pypi/{name}/json", lifetime.Token)); list.Add(name + "：" + json.RootElement.GetProperty("info").GetProperty("version").GetString()); }
            status.Text = string.Join("\n", list) + "\n下载更新由你手动触发，现有环境不会自动改变。";
        });
        runtime.Click += async (_, _) => await Work(async () =>
        {
            if (latest is null) return;
            if (runner.IsRunning || repl.IsRunning || debugger?.IsRunning == true) throw new InvalidOperationException("请先停止 Python。");
            string executable = await new RuntimeUpdates(store.DirectoryPath).DownloadPythonAsync(latest, new Progress<string>(value => status.Text = value), lifetime.Token);
            if (await PythonEnvironments.ProbeAsync(executable, lifetime.Token) is null) throw new IOException("新环境验证失败，保留原解释器。");
            await Install(executable, RuntimeUpdates.ToolPackages); settings.Interpreter = executable; settings.InterpreterSelectedByUser = true; await store.SaveAsync(settings); UpdateInterpreter(); status.Text = "已切换到 Python " + latest;
        });
        tools.Click += async (_, _) => await Work(() => Install(settings.Interpreter, RuntimeUpdates.ToolPackages));
        ml.Click += async (_, _) => await Work(() => Install(settings.Interpreter, ["numpy", "pandas", "matplotlib", "scikit-learn"]));
        fetch.Click += async (_, _) => await Work(async () =>
        {
            fetched = await RuntimeUpdates.FetchConfigurationAsync(source.Text.Trim(), lifetime.Token); ParseColor(fetched.Accent);
            status.Text = $"配置预览\n主题：{fetched.Theme} 材质：{fetched.Material}\n编辑器配色：{fetched.EditorDarkScheme} / {fetched.EditorLightScheme}\n终端配色：{fetched.TerminalDarkScheme} / {fetched.TerminalLightScheme}\n界面字体：{fetched.UiFont} {fetched.UiFontSize} 字重 {fetched.UiFontWeight}\n代码字体：{fetched.EditorFont} {fetched.EditorFontSize} 字重 {fetched.EditorFontWeight}\n终端字体：{fetched.TerminalFont} {fetched.TerminalFontSize} 字重 {fetched.TerminalFontWeight}";
            apply.IsEnabled = true;
        });
        apply.Click += async (_, _) => await SafeAsync(async () =>
        {
            if (fetched is null) return;
            settings.Theme = fetched.Theme; settings.Material = fetched.Material; settings.Accent = fetched.Accent; settings.UseSystemAccent = fetched.UseSystemAccent;
            settings.UiFont = fetched.UiFont; settings.EditorFont = fetched.EditorFont; settings.EditorFontSize = fetched.EditorFontSize;
            settings.UiFontSize = fetched.UiFontSize; settings.UiFontWeight = fetched.UiFontWeight;
            settings.EditorFontWeight = fetched.EditorFontWeight; settings.TerminalFontWeight = fetched.TerminalFontWeight;
            settings.EditorLineHeight = fetched.EditorLineHeight; settings.TerminalLineHeight = fetched.TerminalLineHeight;
            settings.EditorDarkScheme = fetched.EditorDarkScheme; settings.EditorLightScheme = fetched.EditorLightScheme;
            settings.TerminalDarkScheme = fetched.TerminalDarkScheme; settings.TerminalLightScheme = fetched.TerminalLightScheme;
            settings.CustomColorSchemes = fetched.CustomColorSchemes.Select(s => s.Clone()).ToList();
            settings.TerminalFont = fetched.TerminalFont; settings.TerminalFontSize = fetched.TerminalFontSize; settings.IndentSize = fetched.IndentSize; settings.UseTabs = fetched.UseTabs; settings.WordWrap = fetched.WordWrap;
            ApplyAppearance(); await store.SaveAsync(settings); status.Text = "配置已应用并保存。"; apply.IsEnabled = false;
        });
        Add(panel, enabled, check, runtime, tools, ml, source, fetch, apply, status);
        var dialog = new ContentDialog { Title = "环境与在线更新", Content = Scroll(panel), CloseButtonText = "完成" };
        dialog.Closing += (_, args) => { if (downloading) { args.Cancel = true; status.Text = "更新正在进行，请等待完成。"; } };
        await ShowDialogAsync(dialog); lifetime.Dispose(); settings.OnlineUpdates = enabled.IsChecked == true; settings.ConfigurationUrl = source.Text.Trim(); await store.SaveAsync(settings);
    }
}




