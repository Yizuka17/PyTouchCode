using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PyTouchCode.Core;
using System.Text;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    bool deviceRepl;
    readonly SemaphoreSlim deviceOperations = new(1, 1);
    CancellationTokenSource? deviceCommand;
    bool deviceStopping;
    void DeviceLog(string message)
    {
        try { File.AppendAllText(Path.Combine(store.DirectoryPath, "device.log"), $"{DateTimeOffset.Now:O} {message}\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    string DevicePython => File.Exists(bundled) ? bundled! : settings.Interpreter;
    static async Task WaitStoppedAsync(ProcessSession session)
    {
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    async Task<string> MpRemoteAsync(string port, params string[] args)
    {
        port = SerialDevices.ValidatePort(port);
        if (deviceStopping || !await deviceOperations.WaitAsync(0)) throw new InvalidOperationException("设备正在执行任务，请先停止。");
        using var cancellation = new CancellationTokenSource();
        deviceCommand = cancellation;
        var output = new StringBuilder();
        bool runningFile = args.Length == 2 && args[0] == "run";
        void Capture(string text, bool error)
        {
            lock (output)
            {
                output.Append(text);
                // A board program can stream output indefinitely; keep a
                // bounded error tail instead of retaining its entire lifetime.
                if (runningFile && output.Length > 262144) output.Remove(0, output.Length - 262144);
            }
        }
        string? snapshot = null;
        device.Output += Capture;
        try
        {
            // Only the idle REPL may be replaced automatically. A second Run
            // must never kill the command that is already executing.
            if (device.IsRunning && !deviceRepl) throw new InvalidOperationException("设备正在执行任务，请先停止。");
            await WaitStoppedAsync(device);
            cancellation.Token.ThrowIfCancellationRequested();
            deviceRepl = false; selectedPort = port; SelectChannel(3); Notice.IsOpen = false;
            string directory = Path.Combine(store.DirectoryPath, "devices", "port-" + port);
            Directory.CreateDirectory(directory);
            string name = runningFile ? Path.GetFileName(args[1]) : "设备操作";
            if (runningFile)
            {
                // mpremote resets the board before opening the host script.
                // A script on the board's USB disk must be read before reset.
                snapshot = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".py");
                File.Copy(args[1], snapshot);
                args = ["run", snapshot];
                ViewModel.IsRunning = true;
                QueueOutput(3, $"\n运行 {name} ({port})\n");
            }
            ViewModel.SessionStatus = runningFile ? $"正在运行 {name} ({port})" : $"正在访问 {port}";
            DeviceLog($"start {port} {args[0]} {name}");
            int code = await device.RunAsync(DevicePython, new[] { "-u", "-m", "mpremote", "connect", port }.Concat(args), directory, cancellationToken: cancellation.Token);
            DeviceLog($"exit {port} code={code} stopped={cancellation.IsCancellationRequested}");
            cancellation.Token.ThrowIfCancellationRequested();
            if (code != 0)
            {
                string detail = output.ToString().Trim();
                detail = $"{ExitReason(code)}，退出代码 {code} (0x{unchecked((uint)code):X8})。" + (detail.Length == 0 ? "" : "\n" + detail);
                DeviceLog("error " + detail);
                throw new IOException("mpremote 执行失败：" + detail);
            }
            return output.ToString();
        }
        finally
        {
            device.Output -= Capture; deviceCommand = null;
            if (snapshot is not null)
                try { File.Delete(snapshot); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (runningFile) ViewModel.IsRunning = false;
            ViewModel.SessionStatus = "就绪";
            deviceOperations.Release();
        }
    }
    async Task ConnectDeviceAsync(string port)
    {
        if (deviceStopping || !await deviceOperations.WaitAsync(0)) throw new InvalidOperationException("设备正在执行任务，请先停止。");
        try { await StartDeviceReplAsync(port); }
        finally { deviceOperations.Release(); }
    }
    async Task StartDeviceReplAsync(string port)
    {
        if (device.IsRunning) throw new InvalidOperationException("请先断开当前设备会话。");
        selectedPort = SerialDevices.ValidatePort(port); settings.DevicePort = selectedPort; settings.ExecutionTarget = "MicroPython"; UpdateInterpreter();
        await store.SaveAsync(settings); deviceRepl = true; SelectChannel(3);
        _ = SafeAsync(async () => await device.RunAsync(DevicePython, ["-u", Path.Combine(AppContext.BaseDirectory, "tools", "serial_repl.py"), port], store.DirectoryPath));
    }
    async Task InterruptDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedPort)) return;
        await StopDeviceAsync(true);
    }
    async Task StopDeviceAsync(bool interrupt = false)
    {
        if (deviceStopping) return;
        deviceStopping = true;
        try
        {
            DeviceLog($"stop {selectedPort} interrupt={interrupt}");
            deviceCommand?.Cancel();
            await WaitStoppedAsync(device);
            await deviceOperations.WaitAsync();
            try
            {
                deviceRepl = false;
                if (interrupt)
                {
                    await StartDeviceReplAsync(selectedPort);
                    await device.SendAsync(JsonSerializer.Serialize(new { control = 3 }));
                    ViewModel.SessionStatus = "已停止，设备 REPL";
                }
            }
            finally { deviceOperations.Release(); }
        }
        finally { deviceStopping = false; }
    }
    async Task DeviceDialogAsync()
    {
        using var lifetime = new CancellationTokenSource();
        var panel = Form(); var ports = new ComboBox { Header = "USB 串口", IsEditable = true, Text = selectedPort, HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { Name = "DeviceStatus", TextWrapping = TextWrapping.Wrap }; var scan = new Button { Content = "扫描串口" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var connect = new Button { Content = "连接 REPL" }; var disconnect = new Button { Content = "断开" }; actions.Children.Add(connect); actions.Children.Add(disconnect);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var interrupt = new Button { Content = "停止 (Ctrl+C)" }; var reboot = new Button { Content = "软重启 (Ctrl+D)" }; controls.Children.Add(interrupt); controls.Children.Add(reboot);
        var run = new Button { Content = "在设备执行当前文件" };
        var remotePath = Field("设备路径（文件操作使用此路径）", "/main.py");
        var list = new ListView { MaxHeight = 140 }; var listButton = new Button { Content = "列出设备根目录" }; var download = new Button { Content = "下载并打开设备文件" }; var upload = new Button { Content = "上传当前文件到设备路径" };
        var confirmDelete = Check("我确认删除上述设备路径中的文件", false); var delete = new Button { Content = "删除设备文件" };
        remotePath.TextChanged += (_, _) => confirmDelete.IsChecked = false;
        bool operating = false;
        string Port() => SerialDevices.ValidatePort((ports.SelectedItem as SerialDevice)?.Port ?? ports.Text);
        async Task Work(Func<Task> action)
        {
            if (operating) return; operating = true;
            try { await action(); }
            catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "已停止"; }
            catch (Exception e) { status.Text = e.Message; }
            finally { operating = false; }
        }
        async Task Scan()
        {
            scan.IsEnabled = false; status.Text = "正在扫描…";
            try
            {
                var found = await SerialDevices.DiscoverAsync(lifetime.Token);
                string previous = (ports.SelectedItem as SerialDevice)?.Port ?? ports.Text;
                ports.Items.Clear(); foreach (var value in found) ports.Items.Add(value);
                ports.SelectedItem = found.FirstOrDefault(p => string.Equals(p.Port, previous, StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault();
                if (ports.SelectedItem is null) ports.Text = previous;
                status.Text = found.Count == 0 ? "未发现串口，可连接设备后重试。" : $"发现 {found.Count} 个串口";
            }
            finally { scan.IsEnabled = true; }
        }
        scan.Click += async (_, _) => await Work(Scan);
        connect.Click += async (_, _) => await Work(async () => { await ConnectDeviceAsync(Port()); status.Text = "已连接。"; });
        disconnect.Click += async (_, _) => await Work(async () => { await StopDeviceAsync(); status.Text = "已断开"; });
        // Interrupt must remain available while a long mpremote task is running.
        interrupt.Click += async (_, _) => { try { await InterruptDeviceAsync(); status.Text = "已发送中断信号"; } catch (Exception e) { status.Text = e.Message; } };
        reboot.Click += async (_, _) => await Work(async () => { if (!deviceRepl || !device.IsRunning) await InterruptDeviceAsync(); await device.SendAsync(JsonSerializer.Serialize(new { control = 4 })); });
        run.Click += async (_, _) => await Work(async () =>
        {
            if (Active is not { } tab) throw new InvalidOperationException("请先打开文件。");
            if (tab.Document.Path is null || tab.Document.Dirty) throw new InvalidOperationException("请先关闭设备面板并保存当前文件。");
            status.Text = "正在执行。"; await MpRemoteAsync(Port(), "run", tab.Document.Path); status.Text = "设备执行完成";
        });
        listButton.Click += async (_, _) => await Work(async () =>
        {
            string output = await MpRemoteAsync(Port(), "exec", "import os,json;print(json.dumps(list(os.ilistdir('/'))))");
            using var json = JsonDocument.Parse(output.Trim()); list.Items.Clear();
            foreach (var entry in json.RootElement.EnumerateArray()) list.Items.Add(entry[0].GetString() + (entry[1].GetInt32() == 16384 ? "/" : ""));
            status.Text = "根目录已刷新；执行文件操作后需重新连接 REPL。";
        });
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is string name) remotePath.Text = "/" + name; };
        upload.Click += async (_, _) => await Work(async () =>
        {
            if (Active is not { } tab || tab.Document.Path is null || tab.Document.Dirty) throw new InvalidOperationException("请先保存当前文件。");
            await MpRemoteAsync(Port(), "fs", "cp", tab.Document.Path, ":" + remotePath.Text); status.Text = "已上传 " + remotePath.Text;
        });
        download.Click += async (_, _) => await Work(async () =>
        {
            string name = Path.GetFileName(remotePath.Text); if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("请指定文件路径。");
            string directory = Path.Combine(store.DirectoryPath, "devices", "port-" + Port()); Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, name);
            // Download to a temporary file so an open local edit cannot be overwritten silently.
            string staging = target + ".download";
            await MpRemoteAsync(Port(), "fs", "cp", ":" + remotePath.Text, staging);
            if (documents.Any(x => string.Equals(x.Document.Path, target, StringComparison.OrdinalIgnoreCase) && x.Document.Dirty)) { File.Delete(staging); throw new IOException("本地文件有未保存修改，请先保存或关闭标签页。"); }
            File.Move(staging, target, true);
            var existing = documents.FirstOrDefault(x => string.Equals(x.Document.Path, target, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) await ReloadAsync(existing); else await OpenDocumentAsync(target);
            status.Text = "已下载。";
        });
        delete.Click += async (_, _) => await Work(async () =>
        {
            if (confirmDelete.IsChecked != true) throw new InvalidOperationException("请先确认要删除的设备路径。");
            await MpRemoteAsync(Port(), "fs", "rm", ":" + remotePath.Text); confirmDelete.IsChecked = false; status.Text = "已删除 " + remotePath.Text;
        });
        Add(panel, ports, scan, actions, controls, run, remotePath, listButton, list, download, upload, confirmDelete, delete, status);
        var dialog = new ContentDialog { Title = "MicroPython 设备", Content = Scroll(panel), CloseButtonText = "完成" };
        dialog.Opened += async (_, _) => await Work(Scan);
        dialog.Closed += (_, _) => lifetime.Cancel();
        await ShowDialogAsync(dialog);
    }
}



