using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PyTouch.Core;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace PyTouch.App;
public sealed partial class MainWindow
{
    // Opt-in integration harness. Exercises the real WinUI and native editor on
    // the UI thread, with isolated settings; never enabled during normal startup.
    async Task RunSmokeTestsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>(); int failures = 0;
        async Task Test(string name, Func<Task> action)
        {
            try { await action(); results.Add(new { name, passed = true }); }
            catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); }
        }
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Wait(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition()) await Task.Delay(20, timeout.Token);
        }
        async Task Snapshot(UIElement element, string filename)
        {
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
            using var file = File.Create(Path.Combine(directory, filename)); using var stream = file.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 192, 192, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();
        }
        var tab = Active!;
        await Wait(() => tab.Control.IsLoaded && Root.ActualWidth > 1000);
        settings.Completion = false; // Explicit language requests are exercised below.
        await Test("Fluent icons retain their complete original viewport", async () =>
        {
            await Wait(() => AppLogo.Source is BitmapImage { PixelWidth: > 0 });
            var icons = new[] { (Viewbox)SettingsButton.Content, (Viewbox)StopButton.Content, (Viewbox)RefreshFilesButton.Content, (Viewbox)CollapseOutputButton.Content, (Viewbox)((StackPanel)RunButton.Content).Children[0] };
            await Wait(() => icons.All(icon => icon.IsLoaded && icon.ActualWidth > 0 && icon.ActualHeight > 0));
            Assert(icons.All(icon => icon.Child is Canvas { Width: 20, Height: 20 } canvas && canvas.Children[0] is Microsoft.UI.Xaml.Shapes.Path { Data: not null }), "An icon lost its original SVG viewport");
            Assert(tab.Item.IconSource is ImageIconSource { ImageSource: SvgImageSource }, "Tab document icon missing");
            Assert(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "PyTouch.ico")), "Window icon missing");
        });
        await Test("actual toolbar renders in dark and light themes", async () =>
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                settings.Theme = theme; ApplyAppearance(); await Task.Delay(250);
                CommandRow.Background = new SolidColorBrush(ParseColor(theme == "Dark" ? "#202020" : "#F3F3F3"));
                await Task.Delay(50);
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(CommandRow, (int)CommandRow.ActualWidth * 2, (int)CommandRow.ActualHeight * 2);
                byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                Assert(pixels.Any(value => value != 0), "Toolbar render is empty");
                using var file = File.Create(Path.Combine(directory, $"toolbar-{theme.ToLowerInvariant()}-2x.png"));
                using var stream = file.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 192, 192, pixels);
                await encoder.FlushAsync();
                CommandRow.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
                foreach (var console in consoles)
                {
                    Assert(console.Editor.StyleGetBack(32) == tab.Control.Editor.StyleGetBack(32), "Terminal background does not match the palette");
                    for (ulong margin = 0; margin < 5; margin++) Assert(S(console, 2243, margin) == 0, "Terminal still has an editor gutter");
                    Assert(!console.Editor.CaretLineVisible && console.Editor.CaretLineFrame == 0, $"Terminal has a current-line fill or frame: visible={console.Editor.CaretLineVisible}, frame={console.Editor.CaretLineFrame}");
                }
                Assert(tab.Control.Editor.StyleGetBack(33) == tab.Control.Editor.StyleGetBack(32), "Editor gutter does not match its background");
            }
            settings.Theme = "Dark"; ApplyAppearance();
            Assert(Title.Contains("PyTouchCode"), "Window still uses the old product name");
        });
        await Test("UI font preference preserves glyph content and control templates", async () =>
        {
            var glyph = new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe MDL2 Assets") };
            var button = new Button { Content = glyph };
            Root.Children.Add(button);
            try
            {
                await Wait(() => glyph.IsLoaded);
                SetUiFont(Root, new FontFamily("Consolas"));
                Assert(button.FontFamily.Source == "Consolas", "Text control did not receive UI font preference");
                Assert(glyph.FontFamily.Source == "Segoe MDL2 Assets", "UI font overwrote an icon inside a control");
                settings.UiFont = "Segoe UI"; ApplyAppearance();
                Assert(glyph.FontFamily.Source == "Segoe MDL2 Assets", "Changing appearance overwrote an icon font");
            }
            finally { Root.Children.Remove(button); }
        });
        await Test("native layout bounds", () =>
        {
            Assert(Tabs.ActualWidth > 500 && EditorWorkspace.ActualHeight > 300, "Workspace did not arrange");
            Assert(tab.Control.ActualHeight > 100, $"Editor has no height: {tab.Control.ActualHeight}");
            Assert(Root.ActualWidth >= 1000, "Unexpected test viewport"); return Task.CompletedTask;
        });
        await Test("native Python lexer", () =>
        {
            tab.Control.Editor.SetText("def hello():\n    return 42\n"); S(tab.Control, 4003, 0, -1);
            Assert(tab.Control.Editor.GetStyleAt(0) == 5, "Python keyword was not highlighted"); return Task.CompletedTask;
        });
        await Test("native undo and redo", () =>
        {
            var ed = tab.Control.Editor; ed.SetText("value = 1\n"); ed.EmptyUndoBuffer(); ed.GotoPos(ed.Length); ed.ReplaceSel("print(value)\n");
            ed.Undo(); Assert(ed.GetText(ed.Length + 1) == "value = 1\n", "Undo failed");
            ed.Redo(); Assert(ed.GetText(ed.Length + 1).Contains("print(value)"), "Redo failed"); return Task.CompletedTask;
        });
        await Test("native selection and UTF-8 search", () =>
        {
            var ed = tab.Control.Editor; ed.SetText("中文🙂 = 42\n中文\n"); ed.TargetStart = 0; ed.TargetEnd = ed.Length;
            Assert(ed.SearchInTarget(Encoding.UTF8.GetByteCount("中文"), "中文") == 0, "Unicode search failed");
            ed.SetSel(ed.TargetStart, ed.TargetEnd); Assert(ed.GetSelText() == "中文", "Unicode selection corrupted"); return Task.CompletedTask;
        });
        await Test("comment toggling is reversible", () =>
        {
            var ed = tab.Control.Editor; const string source = "a = 1\n    b = 2\n"; ed.SetText(source); ed.SelectAll(); ToggleComment(tab);
            Assert(ed.GetText(ed.Length + 1).Contains("# a"), "Comment not inserted"); ed.SelectAll(); ToggleComment(tab);
            Assert(ed.GetText(ed.Length + 1) == source, "Uncomment did not restore text"); return Task.CompletedTask;
        });
        await Test("auto indentation and paired brackets", () =>
        {
            var ed = tab.Control.Editor; ed.SetText("if True:\n"); ed.GotoPos(ed.Length); AutoPairAndIndent(tab, '\n');
            Assert(ed.GetText(ed.Length + 1).EndsWith("    "), "Indentation missing");
            ed.SetText("("); ed.GotoPos(1); AutoPairAndIndent(tab, '('); Assert(ed.GetText(ed.Length + 1) == "()", "Bracket pair missing"); return Task.CompletedTask;
        });
        await Test("file save, close and reload through app", async () =>
        {
            tab.Document.Path = Path.Combine(directory, "native-save.py"); tab.Control.Editor.SetText("print('保存🙂')\n");
            await SaveDocumentAsync(tab); Assert((await File.ReadAllTextAsync(tab.Document.Path)).Contains("保存🙂"), "Save lost Unicode");
            Assert(!tab.Document.Dirty, "Save point remained dirty"); await ReloadAsync(tab);
            Assert(tab.Control.Editor.GetText(tab.Control.Editor.Length + 1).Contains("保存🙂"), "Reload failed");
            int count = documents.Count; await OpenDocumentAsync(tab.Document.Path); Assert(documents.Count == count, "Duplicate file tab opened");
        });
        await Test("independent editor tabs", async () =>
        {
            tab.Control.Editor.GotoPos(tab.Control.Editor.Length); tab.Control.Editor.ReplaceSel("# unsaved edit\n");
            NewDocument("second tab\n"); var second = Active!; await Wait(() => second.Control.IsLoaded);
            Assert(second.Control.Editor.GetText(second.Control.Editor.Length + 1) == "second tab\n", "New tab text missing");
            Assert(tab.Control.Editor.GetText(tab.Control.Editor.Length + 1).Contains("保存"), "First document was lost");
            Tabs.SelectedItem = tab.Item;
            await Wait(() => tab.Control.IsLoaded);
            Assert(tab.Control.Editor.GetText(tab.Control.Editor.Length + 1).Contains("# unsaved edit"), "Tab switch discarded unsaved edits");
        });
        await Test("light and dark palettes are distinct", async () =>
        {
            settings.Theme = "Dark"; ApplyAppearance(); await Wait(() => Root.ActualTheme == ElementTheme.Dark);
            int dark = tab.Control.Editor.StyleGetBack(32);
            settings.Theme = "Light"; ApplyAppearance(); await Wait(() => Root.ActualTheme == ElementTheme.Light);
            int light = tab.Control.Editor.StyleGetBack(32); Assert(dark != light, "Theme did not change native editor colors");
        });
        await Test("native font and wrap settings", () =>
        {
            settings.EditorFont = "Consolas"; settings.EditorFontSize = 17; settings.WordWrap = true; ApplyAppearance();
            Assert(tab.Control.Editor.StyleGetFont(32) == "Consolas" && tab.Control.Editor.StyleGetSizeFractional(32) == 1700, "Font preference ignored");
            Assert((int)tab.Control.Editor.WrapMode == 1, "Wrap not enabled"); return Task.CompletedTask;
        });
        await Test("all palettes apply independently without changing editor or terminal input", () =>
        {
            string original = JsonSerializer.Serialize(settings); string source = tab.Control.Editor.GetText(tab.Control.Editor.Length + 1);
            try
            {
                settings.Theme = "Dark";
                ReplaceTerminalDraft(0, "保留输入🙂");
                foreach (var scheme in ColorSchemes.BuiltIn)
                {
                    settings.EditorDarkScheme = scheme.Name; settings.TerminalDarkScheme = "Solarized Light"; ApplyAppearance();
                    Assert(tab.Control.Editor.StyleGetBack(32) == ColorRef(scheme.Background), "Editor palette did not apply: " + scheme.Name);
                    Assert(consoles[0].Editor.StyleGetBack(32) == ColorRef(ColorSchemes.Resolve(settings, "Solarized Light").Background), "Terminal palette followed the editor unexpectedly");
                }
                Assert(TerminalDraft(0) == "保留输入🙂" && tab.Control.Editor.GetText(tab.Control.Editor.Length + 1) == source, "Appearance change modified input or the document");
            }
            finally { ReplaceTerminalDraft(0, ""); settings = JsonSerializer.Deserialize<AppSettings>(original)!; ApplyAppearance(); }
            return Task.CompletedTask;
        });
        await Test("native font list, independent font weights and line heights", async () =>
        {
            Assert(FontCatalog.Families.Count > 10 && FontCatalog.Families.Contains("Consolas"), "Installed font list is empty or not from this machine");
            string original = JsonSerializer.Serialize(settings);
            var list = FontChoice("编辑器字体", "Consolas"); var size = Number("字号", 17, 9, 40); var weight = WeightChoice("字重", 700); var sample = FontPreview(list, size, weight, ColorSchemes.ForEditor(settings, Root.ActualTheme == ElementTheme.Dark));
            var panel = new StackPanel { Spacing = 12, Width = 460, Background = new SolidColorBrush(ParseColor("#202020")), Padding = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Add(panel, list, size, weight, sample); Grid.SetRow(panel, 2); Root.Children.Add(panel);
            try
            {
                await Wait(() => panel.IsLoaded);
                settings.UiFont = "Segoe UI"; settings.UiFontSize = 16; settings.UiFontWeight = 600;
                settings.EditorFont = "Consolas"; settings.EditorFontWeight = 700; settings.EditorLineHeight = 1.5;
                settings.TerminalFont = "Consolas"; settings.TerminalFontWeight = 500; settings.TerminalLineHeight = 1.25; ApplyAppearance();
                Assert(RunButton.FontWeight.Weight == 600 && MainMenu.FontSize == 16, "UI typography was not applied");
                Assert((int)tab.Control.Editor.StyleGetWeight(32) == 700 && (int)tab.Control.Editor.StyleGetWeight(5) == 700 && (int)consoles[0].Editor.StyleGetWeight(32) == 500, "Editor and terminal font weights were lost or coupled");
                Assert(S(tab.Control, 2526) + S(tab.Control, 2528) > 0 && S(consoles[0], 2526) + S(consoles[0], 2528) > 0, "Line height was not applied");
                weight.SelectedIndex = 5; weight.SelectedIndex = 6;
                panel.Background = new SolidColorBrush(ParseColor(Root.ActualTheme == ElementTheme.Dark ? "#202020" : "#F3F3F3"));
                Assert(FontName(list) == "Consolas" && ((TextBlock)sample.Child).FontWeight.Weight == 700 && sample.Background is SolidColorBrush, "Font picker or terminal-style preview ignored the choice");
                await Snapshot(panel, "font-settings.png");
            }
            finally { Root.Children.Remove(panel); settings = JsonSerializer.Deserialize<AppSettings>(original)!; ApplyAppearance(); }
        });
        await Test("palette list, custom color staging and native preview", async () =>
        {
            var options = new SchemeSettings(settings);
            var host = new Border { Width = 560, Height = 650, Padding = new Thickness(20), Background = new SolidColorBrush(ParseColor(Root.ActualTheme == ElementTheme.Dark ? "#202020" : "#F3F3F3")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Scroll(options.View) };
            Grid.SetRow(host, 2); Root.Children.Add(host);
            try
            {
                await Wait(() => host.IsLoaded);
                Assert(options.EditorDark.Items.Count >= 16 && options.TerminalLight.Items.Count >= 16, "Palette dropdowns are incomplete");
                options.EditorDark.SelectedItem = "Solarized Dark"; options.TerminalDark.SelectedItem = "Campbell";
                var custom = ColorSchemes.BuiltIn[0].Clone(); custom.Name = "UI custom"; custom.Background = "#123456"; options.Custom.Add(custom);
                var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!; options.Apply(candidate); candidate.Validate();
                Assert(candidate.EditorDarkScheme == "Solarized Dark" && candidate.TerminalDarkScheme == "Campbell" && candidate.CustomColorSchemes.Any(s => s.Name == "UI custom"), "Palette controls lost staged changes");
                Assert(!settings.CustomColorSchemes.Any(s => s.Name == "UI custom"), "Opening or editing the dialog mutated live settings before Save");
                await Snapshot(host, "palette-settings.png");
            }
            finally { Root.Children.Remove(host); }
        });
        await Test("file icons load, folders expand and renamed tabs update", async () =>
        {
            string folder = Path.Combine(directory, "icon-types"); Directory.CreateDirectory(Path.Combine(folder, "subfolder"));
            foreach (string name in new[] { "MAIN.PY", "data.csv", "logic.cpp", "notes.unknown" }) await File.WriteAllTextAsync(Path.Combine(folder, name), "# icon test\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "subfolder", "nested.py"), "print('nested')\n");
            settings.Workspace = folder; await RefreshFilesAsync();
            var root = FileTree.RootNodes[0];
            Assert(root.Content is FileTreeEntry && root.Children.All(n => n.Content is FileTreeEntry), "Tree nodes must contain file data rather than shared UI elements");
            root.IsExpanded = false; await Task.Delay(50);
            Assert(((SvgImageSource)((FileTreeEntry)root.Content).IconSource!).UriSource.AbsolutePath.EndsWith("folder.svg"), "Collapsed folder icon did not update");
            root.IsExpanded = true; await Task.Delay(50);
            Assert(((SvgImageSource)((FileTreeEntry)root.Content).IconSource!).UriSource.AbsolutePath.EndsWith("folder-open.svg"), "Expanded folder icon did not update");
            // Regression: load nested nodes by expanding the actual live TreeView.
            var subfolder = root.Children.First(n => n.Content is FileTreeEntry { Name: "subfolder" });
            Assert(subfolder.HasUnrealizedChildren, "Subfolder should initially be lazy");
            subfolder.IsExpanded = true;
            await Wait(() => subfolder.Children.Count > 0);
            Assert(subfolder.Children[0].Content is FileTreeEntry { Name: "nested.py", Directory: false }, "Expanding subfolder did not load its Python file");
            await OpenDocumentAsync(Path.Combine(folder, "data.csv")); var csv = Active!;
            Assert(csv.IconKey == "table", "CSV tab did not use the table icon");
            await SaveDocumentToPathAsync(csv, Path.Combine(folder, "renamed.json"));
            Assert(csv.IconKey == "json", "Save under a new extension did not refresh the tab icon");
            Tabs.SelectedItem = tab.Item;
            var keys = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "FileIcons"), "*.svg").Select(Path.GetFileNameWithoutExtension).ToArray();
            var gallery = new Grid { Width = 570, Padding = new Thickness(12), Background = new SolidColorBrush(ParseColor(Root.ActualTheme == ElementTheme.Dark ? "#202020" : "#F3F3F3")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ColumnSpacing = 12, RowSpacing = 12 };
            for (int i = 0; i < 3; i++) gallery.ColumnDefinitions.Add(new ColumnDefinition());
            int opened = 0; var errors = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (i % 3 == 0) gallery.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var image = new Image { Width = 24, Height = 24 };
                image.ImageOpened += (_, _) => opened++; image.ImageFailed += (_, e) => errors.Add(e.ErrorMessage);
                image.Source = FileImage(keys[i]!);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(image); row.Children.Add(new TextBlock { Text = keys[i], FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetRow(row, i / 3); Grid.SetColumn(row, i % 3); gallery.Children.Add(row);
            }
            Grid.SetRow(gallery, 2); Root.Children.Add(gallery);
            try
            {
                await Wait(() => opened == keys.Length || errors.Count > 0);
                Assert(errors.Count == 0, "SVG icon failed: " + string.Join(", ", errors));
                await Snapshot(gallery, "file-icon-gallery.png");
            }
            finally { Root.Children.Remove(gallery); }
        });
        await Test("breakpoints track inserted lines", async () =>
        {
            var ed = tab.Control.Editor; ed.SetText("x = 1\nprint(x)\n"); await ToggleBreakpointAsync(tab, 1);
            Assert(BreakpointLines(tab).SequenceEqual(new[] { 2 }), "Breakpoint not set"); ed.InsertText(0, "# inserted\n");
            Assert(BreakpointLines(tab).SequenceEqual(new[] { 3 }), "Breakpoint did not follow line"); await ToggleBreakpointAsync(tab, 2);
        });
        await Test("native completion popup and ghost text", async () =>
        {
            var ed = tab.Control.Editor; ed.AutoCCancel(); ed.SetText("pri"); ed.GotoPos(ed.Length); tab.Control.Focus(FocusState.Programmatic); await CompleteAsync(tab, false);
            Assert(tab.GhostInsertion.Length > 0 && tab.Ghost.Visibility == Visibility.Visible, $"Ghost completion not displayed: insertion={tab.GhostInsertion}, visibility={tab.Ghost.Visibility}, popup={ed.AutoCActive()}, pos={ed.CurrentPos}, length={ed.Length}, interpreter={settings.Interpreter}");
            await CompleteAsync(tab, true); Assert(ed.AutoCActive(), "Native completion popup not opened"); ed.AutoCCancel();
        });
        await Test("selection handle positions", () =>
        {
            var ed = tab.Control.Editor; ed.SetText("selection handles\n"); ed.SetSel(0, 9); ed.ScrollCaret(); tab.ShowHandles = true; UpdateSelectionHandles(tab);
            Assert(tab.StartHandle.Visibility == Visibility.Visible && tab.EndHandle.Visibility == Visibility.Visible, $"Touch handles were not displayed: start={ed.SelectionStart},end={ed.SelectionEnd},x={ed.PointXFromPosition(0)},y={ed.PointYFromPosition(0)},h={ed.TextHeight(0)},size={tab.Control.ActualWidth}x{tab.Control.ActualHeight},scale={tab.Control.XamlRoot.RasterizationScale}");
            ed.SetEmptySelection(0); UpdateSelectionHandles(tab); Assert(tab.StartHandle.Visibility == Visibility.Collapsed, "Collapsed selection kept a handle");
            return Task.CompletedTask;
        });
        await Test("real script execution reaches native output pane", async () =>
        {
            tab.Control.Editor.SetText("print('UI-SMOKE-42')\n"); await RunFileAsync(false); FlushOutput();
            Assert(consoles[0].Editor.GetText(consoles[0].Editor.Length + 1).Contains("UI-SMOKE-42"), "Script output did not reach editor console");
        });
        await Test("inline terminal protects history and preserves pending Unicode input", () =>
        {
            ClearTerminal(0); AppendTerminalOutput(0, "提示：");
            ReplaceTerminalDraft(0, "中文🙂 draft");
            var ed = consoles[0].Editor; ed.GotoPos(ed.PositionRelative(terminalStates[0].InputStart, 2));
            long offset = ed.CurrentPos - terminalStates[0].InputStart;
            AppendTerminalOutput(0, "异步输出\n");
            Assert(TerminalDraft(0) == "中文🙂 draft", "Asynchronous output overwrote pending input");
            Assert(ed.CurrentPos - terminalStates[0].InputStart == offset, "Asynchronous output moved the draft cursor");
            ed.SetSel(0, 3); ProtectTerminal(0); string before = ed.GetText(ed.Length + 1); ed.ReplaceSel("BAD");
            Assert(ed.ReadOnly && ed.GetText(ed.Length + 1) == before, "Output history was editable");
            ReplaceTerminalDraft(0, ""); return Task.CompletedTask;
        });
        await Test("Python input is submitted directly from the terminal", async () =>
        {
            SelectChannel(0); ClearTerminal(0);
            var task = runner.RunAsync(settings.Interpreter, ["-u", "-c", "name=input('姓名：'); print('你好 '+name)"], directory);
            await Wait(() => { FlushOutput(); return consoles[0].Editor.GetText(consoles[0].Editor.Length + 1).Contains("姓名："); });
            ReplaceTerminalDraft(0, "测试🙂"); await SubmitTerminalAsync(0); await task; FlushOutput();
            Assert(consoles[0].Editor.GetText(consoles[0].Editor.Length + 1).Contains("你好 测试🙂"), "Inline Unicode input did not reach Python");
        });
        await Test("Shell and Python REPL accept inline commands", async () =>
        {
            await StartShellAsync(); await Wait(() => { FlushOutput(); return shell.IsRunning && consoles[1].Editor.GetText(consoles[1].Editor.Length + 1).Contains("PS "); });
            ReplaceTerminalDraft(1, "Write-Output ('SHELL-' + 'DIRECT-42')"); await SubmitTerminalAsync(1);
            await Wait(() => { FlushOutput(); return consoles[1].Editor.GetText(consoles[1].Editor.Length + 1).Contains("SHELL-DIRECT-42"); });
            string shellText = consoles[1].Editor.GetText(consoles[1].Editor.Length + 1);
            Assert(shellText.Split("Write-Output ('SHELL-' + 'DIRECT-42')").Length == 2, "PowerShell command was echoed more than once");
            ReplaceTerminalDraft(1, "exit"); await SubmitTerminalAsync(1); await Wait(() => !shell.IsRunning);
            await StartReplAsync(); await Wait(() => { FlushOutput(); return repl.IsRunning && consoles[2].Editor.GetText(consoles[2].Editor.Length + 1).Contains(">>> "); });
            ReplaceTerminalDraft(2, "print('REPL-' + 'DIRECT-42')"); await SubmitTerminalAsync(2);
            await Wait(() => { FlushOutput(); return consoles[2].Editor.GetText(consoles[2].Editor.Length + 1).Replace("\r\n", "\n").Contains("REPL-DIRECT-42\n>>> "); });
            Assert(terminalStates[2].History.Contains("print('REPL-' + 'DIRECT-42')"), "REPL history was not recorded");
            ReplaceTerminalDraft(2, "未发送草稿🙂"); MoveTerminalHistory(2, -1);
            Assert(TerminalDraft(2) == "print('REPL-' + 'DIRECT-42')", "History navigation did not recall the previous command");
            MoveTerminalHistory(2, 1); Assert(TerminalDraft(2) == "未发送草稿🙂", "History navigation lost the pending draft");
            ReplaceTerminalDraft(2, "exit()"); await SubmitTerminalAsync(2); await Wait(() => !repl.IsRunning); FlushOutput();
            foreach (int channel in new[] { 0, 1, 2 }) await File.WriteAllTextAsync(Path.Combine(directory, $"terminal-{channel}.txt"), consoles[channel].Editor.GetText(consoles[channel].Editor.Length + 1));
        });
        await Test("lazy file tree", async () =>
        {
            settings.Workspace = directory; await RefreshFilesAsync(); Assert(FileTree.RootNodes.Count == 1 && FileTree.RootNodes[0].Children.Count > 0, "File tree did not load");
        });
        await Test("panel visibility and settings persistence", async () =>
        {
            ShowPanel(false); Assert(PanelHeight.Height.Value == 0, "Panel did not collapse"); ShowPanel(true);
            ShowSidebar(false); Assert(SidebarWidth.Width.Value == 0, "Sidebar did not collapse"); ShowSidebar(true);
            await store.SaveAsync(settings); var loaded = await store.LoadAsync(); Assert(loaded.EditorFont == "Consolas" && loaded.EditorFontSize == 17, "UI settings failed to persist");
        });
        await File.WriteAllTextAsync(Path.Combine(directory, "ui-smoke.json"), JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, viewport = new { width = Root.ActualWidth, height = Root.ActualHeight }, results }, SettingsStore.JsonOptions));
        runner.Stop(); shell.Stop(); repl.Stop(); device.Stop(); language.Stop(); closing = true; Close();
    }
}

