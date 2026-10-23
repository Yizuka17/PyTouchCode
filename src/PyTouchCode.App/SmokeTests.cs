using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PyTouchCode.Core;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    interface ITouchTsfThreadManager
    {
        void Activate(out uint id);
        void Deactivate();
        void CreateDocumentMgr(out nint document);
        void EnumDocumentMgrs(out nint documents);
        void GetFocus(out nint document);
    }
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("AA80E7F4-2021-11D2-93E0-0060B067B86E"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    interface ITouchTsfDocument
    {
        void CreateContext(uint client, uint flags, nint owner, out nint context, out uint cookie);
        void Push(nint context);
        void Pop(uint flags);
        [System.Runtime.InteropServices.PreserveSig] int GetTop(ref nint context);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct TouchTsfStatus { public uint DynamicFlags, StaticFlags; }
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("AA80E7FD-2021-11D2-93E0-0060B067B86E"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    interface ITouchTsfContext
    {
        void RequestEditSession(uint client, nint session, uint flags, out int result);
        void InWriteSession(uint client, out int writing);
        void GetSelection(uint cookie, uint index, uint count, nint selection, out uint fetched);
        void SetSelection(uint cookie, uint count, nint selection);
        void GetStart(uint cookie, out nint range);
        void GetEnd(uint cookie, out nint range);
        void GetActiveView(out nint view);
        void EnumViews(out nint views);
        void GetStatus(out TouchTsfStatus status);
    }
    ITouchTsfThreadManager? touchProbeManager;
    nint CurrentNativeTextDocument()
    {
        if (touchProbeManager is null)
        {
            touchProbeManager = (ITouchTsfThreadManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50"))!)!;
            Closed += (_, _) => { if (touchProbeManager is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(touchProbeManager); touchProbeManager = null; };
        }
        touchProbeManager.GetFocus(out nint document); return document;
    }
    bool HasEditableNativeInput()
    {
        nint document = CurrentNativeTextDocument();
        if (document == 0) return false;
        object manager = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(document);
        try
        {
            nint context = 0;
            int result = ((ITouchTsfDocument)manager).GetTop(ref context);
            if (result < 0) System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
            if (context == 0) return false;
            object nativeContext = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(context);
            try
            {
                TouchTsfStatus status;
                try { ((ITouchTsfContext)nativeContext).GetStatus(out status); }
                catch (System.Runtime.InteropServices.COMException e) when (e.HResult is unchecked((int)0x80040509) or unchecked((int)0x80040504))
                {
                    // Windows can expose a placeholder context for an empty document.
                    File.AppendAllText(Path.Combine(SmokeDirectory!, "tsf-status.txt"), $"document=0x{document:X} neutral=0x{gestureInputDocument:X} input=none hresult=0x{e.HResult:X}\n");
                    return false;
                }
                File.AppendAllText(Path.Combine(SmokeDirectory!, "tsf-status.txt"), $"document=0x{document:X} neutral=0x{gestureInputDocument:X} context=0x{context:X} result={result} dynamic={status.DynamicFlags} static={status.StaticFlags}\n");
                return (status.DynamicFlags & 1) == 0;
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(nativeContext); System.Runtime.InteropServices.Marshal.Release(context); }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(manager);
            System.Runtime.InteropServices.Marshal.Release(document);
        }
    }
    // Opt-in integration harness. Exercises the real WinUI and native editor on
    // the UI thread, with isolated settings; never enabled during normal startup.
    async Task RunSmokeTestsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>(); int failures = 0;
        string? profilePython = null;
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
            Assert(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "PyTouchCode.ico")), "Window icon missing");
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
        await Test("visible menu headers follow UI typography after font and theme switches", async () =>
        {
            string original = JsonSerializer.Serialize(settings);
            try
            {
                foreach (string family in new[] { "Consolas", "Microsoft YaHei", "Segoe UI" })
                {
                    settings.UiFont = family; settings.UiFontSize = 19; settings.UiFontWeight = 700;
                    settings.Theme = family == "Microsoft YaHei" ? "Light" : "Dark"; ApplyAppearance();
                    await Task.Delay(100);
                    foreach (var menu in MainMenu.Items)
                    {
                        var button = MenuHeaderButton(menu);
                        Assert(button is { IsLoaded: true } && button.FontFamily.Source == family && button.FontSize == 19 && button.FontWeight.Weight == 700, "Visible menu header ignored typography: " + menu.Title);
                    }
                    CommandRow.Background = new SolidColorBrush(ParseColor(Root.ActualTheme == ElementTheme.Dark ? "#202020" : "#F3F3F3"));
                    await Snapshot(CommandRow, "menu-font-" + family.Replace(' ', '-') + ".png");
                    CommandRow.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
                }
            }
            finally { CommandRow.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty); settings = JsonSerializer.Deserialize<AppSettings>(original)!; ApplyAppearance(); }
        });
        await Test("native material transparency changes the backdrop without fading content", async () =>
        {
            string original = JsonSerializer.Serialize(settings);
            try
            {
                foreach (string material in new[] { "Acrylic", "Mica", "MicaAlt", "Solid" })
                {
                    bool supported = material == "Acrylic" ? Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() : material != "Solid" && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported();
                    settings.Material = material; settings.MaterialTransparency = .2; ApplyAppearance();
                    if (!supported) { Assert(SystemBackdrop is null, "Unsupported material has no solid fallback"); continue; }
                    await Wait(() => (SystemBackdrop as AdjustableBackdrop)?.TintOpacity.HasValue == true);
                    var backdrop = (AdjustableBackdrop)SystemBackdrop!;
                    Assert(Math.Abs(backdrop.TintOpacity!.Value - .8) < .001, "Material tint ignored transparency");
                    ApplyWindowMaterial(material, .85);
                    Assert(ReferenceEquals(backdrop, SystemBackdrop) && Math.Abs(backdrop.TintOpacity!.Value - .15) < .001, "Slider did not update the connected controller");
                    settings.Theme = "Light"; ApplyAppearance(); settings.Theme = "Dark"; ApplyAppearance();
                    Assert(Root.Opacity == 1 && MainMenu.Opacity == 1 && tab.Control.Opacity == 1, "Transparency faded window content");
                }
            }
            finally { settings = JsonSerializer.Deserialize<AppSettings>(original)!; ApplyAppearance(); }
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
        await Test("bracket pairing can be disabled without disabling indentation", () =>
        {
            var ed = tab.Control.Editor; bool original = settings.AutoCloseBrackets;
            try
            {
                foreach (bool enabled in new[] { false, true })
                {
                    settings.AutoCloseBrackets = enabled;
                    foreach (var pair in new[] { ('(', ')'), ('[', ']'), ('{', '}'), ('\'', '\''), ('"', '"') })
                    {
                        string text = "value = " + pair.Item1;
                        ed.SetText(text); ed.SetEmptySelection(ed.Length); AutoPairAndIndent(tab, pair.Item1);
                        Assert(ed.GetText(ed.Length + 1) == text + (enabled ? pair.Item2.ToString() : ""), "Pair setting was ignored");
                        Assert(ed.CurrentPos == text.Length, "Pair setting moved the caret");
                    }
                    ed.SetText("def outer():\n"); ed.GotoPos(ed.Length); AutoPairAndIndent(tab, '\n');
                    Assert(ed.GetLine(1) == new string(' ', settings.IndentSize), "Pair setting disabled newline indentation");
                }
            }
            finally { settings.AutoCloseBrackets = original; }
            return Task.CompletedTask;
        });
        await Test("backspace removes auto indentation to column zero between functions", () =>
        {
            var ed = tab.Control.Editor; bool tabs = settings.UseTabs; int size = settings.IndentSize;
            try
            {
                foreach (bool useTabs in new[] { false, true })
                {
                    settings.UseTabs = useTabs; settings.IndentSize = 4; ConfigureEditor(tab.Control, false);
                    string indent = useTabs ? "\t\t" : "        ";
                    ed.SetText("def first():\n    def inner():\n" + indent + "pass\n\ndef second():\n    pass\n");
                    ed.SetEmptySelection(ed.GetLineEndPosition(2));
                    S(tab.Control, 2329); // Native Enter, including CharAdded.
                    long line = ed.LineFromPosition(ed.CurrentPos);
                    Assert(ed.GetColumn(ed.CurrentPos) == 8, "Enter did not inherit indentation");
                    Assert(BackspaceIndent(tab.Control) && ed.GetColumn(ed.CurrentPos) == 4 && ed.LineFromPosition(ed.CurrentPos) == line, "First Backspace did not dedent one level");
                    Assert(BackspaceIndent(tab.Control) && ed.GetColumn(ed.CurrentPos) == 0 && ed.LineFromPosition(ed.CurrentPos) == line, "Backspace stopped at the previous function's indentation");
                    ed.ReplaceSel("print('outside')");
                    Assert(ed.GetLine(line).StartsWith("print('outside')") && ed.GetText(ed.Length + 1).Contains("def second():"), "Dedenting changed another function");
                }
            }
            finally { settings.UseTabs = tabs; settings.IndentSize = size; ConfigureEditor(tab.Control, false); }
            return Task.CompletedTask;
        });
        await Test("backspace inside nested indentation preserves text to the right and undo", () =>
        {
            var ed = tab.Control.Editor; int size = settings.IndentSize;
            try
            {
                settings.IndentSize = 4; ConfigureEditor(tab.Control, false);
                foreach (string whitespace in new[] { "            ", "\t\t\t", "  \t    \t" })
                {
                    string source = "def outer():\n    def inner():\n" + whitespace + "print('你好🙂')\n";
                    ed.SetText(source); ed.EmptyUndoBuffer();
                    long start = ed.PositionFromLine(2);
                    int prefix = whitespace.StartsWith('\t') ? 1 : whitespace.StartsWith("  \t") ? 3 : 4;
                    ed.SetEmptySelection(start + prefix);
                    string suffix = source[(source.IndexOf(whitespace + "print", StringComparison.Ordinal) + prefix)..];
                    Assert(BackspaceIndent(tab.Control), "Indent Backspace was not handled");
                    Assert(ed.CurrentPos == start && ed.GetText(ed.Length + 1).EndsWith(suffix), "Backspace moved right or changed the suffix");
                    ed.Undo(); Assert(ed.GetText(ed.Length + 1) == source, "Undo did not restore the indentation");
                }
                ed.SetText("    pass\n"); ed.SetSel(1, 3);
                Assert(!BackspaceIndent(tab.Control), "Selected text must use normal Backspace");
                ed.SetEmptySelection(8); Assert(!BackspaceIndent(tab.Control), "Normal code must use character deletion");
                ed.SetEmptySelection(0); Assert(!BackspaceIndent(tab.Control), "Column zero must allow normal line joining");
            }
            finally { settings.IndentSize = size; ConfigureEditor(tab.Control, false); }
            return Task.CompletedTask;
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
        await Test("folder menus, nested creation and rename preserve open unsaved documents", async () =>
        {
            string workspace = WorkspaceFiles.Create(directory, "file-actions", true);
            string nested = WorkspaceFiles.Create(workspace, "nested", true);
            string file = WorkspaceFiles.Create(nested, "hello.py", false);
            await File.WriteAllTextAsync(file, "print('original')");
            settings.Workspace = workspace; await RefreshFilesAsync();
            var menu = CreateFileMenu(new FileTreeEntry(nested, true));
            string[] actions = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            Assert(new[] { "新建文件", "新建文件夹", "重命名", "在资源管理器中显示", "复制路径", "刷新" }.All(actions.Contains), "Folder context menu is incomplete");
            bool opened = false; menu.Opened += (_, _) => opened = true; menu.ShowAt(FileTree);
            await Wait(() => opened); await Snapshot(Sidebar, "folder-menu.png"); menu.Hide();
            await OpenDocumentAsync(file); var openedTab = Active!; await Wait(() => openedTab.Initialized);
            openedTab.Control.Editor.AppendText(9, "# unsaved");
            await RenameEntryAsync(nested, "renamed");
            string renamedFile = Path.Combine(workspace, "renamed", "hello.py");
            Assert(openedTab.Document.Path == renamedFile && openedTab.Document.Dirty && openedTab.Control.Editor.GetText(openedTab.Control.Editor.Length + 1).EndsWith("# unsaved"), "Folder rename lost open document state");
            Assert(File.ReadAllText(renamedFile) == "print('original')", "Rename overwrote the file with unsaved content");
            await RenameEntryAsync(renamedFile, "hello.json");
            Assert(openedTab.IconKey == "json", "Rename did not update the tab icon");
            await RenameEntryAsync(workspace, "renamed-workspace");
            Assert(settings.Workspace == Path.Combine(directory, "renamed-workspace") && openedTab.Document.Path!.StartsWith(settings.Workspace), "Workspace root rename lost open paths");
            await SaveDocumentToPathAsync(openedTab, openedTab.Document.Path!);
            await CloseTabAsync(openedTab); Tabs.SelectedItem = tab.Item;
        });
        await Test("touch blank-space hit testing stays on its line and panning preserves selection", async () =>
        {
            var ed = tab.Control.Editor; ed.SetText("first\nsecond\nthird\n"); ed.SetEmptySelection(0); ed.ScrollCaret();
            await Task.Delay(50);
            double scale = tab.Control.XamlRoot.RasterizationScale;
            long second = ed.PositionFromLine(1);
            var blank = new Windows.Foundation.Point((ed.PointXFromPosition(ed.GetLineEndPosition(1)) + 180) / scale, (ed.PointYFromPosition(second) + ed.TextHeight(1) / 2.0) / scale);
            long hit = TouchPosition(tab.Control, blank);
            Assert(ed.LineFromPosition(hit) == 1 && hit == ed.GetLineEndPosition(1), "Blank space touch jumped to another line");
            Assert(TouchPosition(tab.Control, new Windows.Foundation.Point(blank.X, blank.Y + 150)) == ed.Length, "Touch below document did not resolve to its end");
            ed.SetText(string.Concat(Enumerable.Range(0, 200).Select(n => $"line {n}\n"))); ed.SetSel(10, 15); ed.ScrollCaret();
            long first = ed.FirstVisibleLine, originalAnchor = ed.Anchor, caret = ed.CurrentPos;
            // Model the native child moving selection before the routed touch event.
            ed.SetSel(10, 80); PanTouch(tab.Control, originalAnchor, caret, first, 0, 12, -160);
            Assert(ed.FirstVisibleLine > first && ed.Anchor == originalAnchor && ed.CurrentPos == caret, "Pan changed selection or failed to scroll");
            ed.SetSel(10, 110); PanTouch(tab.Control, originalAnchor, caret, first, 0, -14, -220);
            Assert(ed.Anchor == originalAnchor && ed.CurrentPos == caret, "A diagonal swipe selected text");
            ed.SetEmptySelection(0); ed.ScrollCaret();
        });
        await Test("touch rendering surface defers editor and terminal focus until a confirmed tap", async () =>
        {
            SelectChannel(0);
            bool originalWrap = settings.WordWrap;
            double originalEditorFont = settings.EditorFontSize, originalTerminalFont = settings.TerminalFontSize;
            settings.WordWrap = false; ApplyAppearance();
            try
            {
            foreach (var control in new[] { tab.Control, consoles[0] })
            {
                IEnumerable<string> Describe(DependencyObject node)
                {
                    yield return node is FrameworkElement f ? $"{node.GetType().Name} name={f.Name} loaded={f.IsLoaded} size={f.ActualWidth}x{f.ActualHeight}" : node.GetType().Name;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                        foreach (string child in Describe(VisualTreeHelper.GetChild(node, i))) yield return "  " + child;
                }
                await File.WriteAllLinesAsync(Path.Combine(directory, control == tab.Control ? "touch-editor-tree.txt" : "touch-terminal-tree.txt"), Describe(control));
                await Wait(() => control.IsLoaded && control.ActualHeight > 100 && touchInputs.TryGetValue(control, out var input) && input.Surface is not null);
                var gate = touchInputs.GetValue(control, _ => throw new InvalidOperationException("Touch gate missing"));
                Assert(gate.Surface is { Name: "ImageTarget", IsHoldingEnabled: false } && !ReferenceEquals(gate.Surface, control), "Touch is still intercepted after the native editor's focus handler");
                var ed = control.Editor;
                if (control == consoles[0]) { ClearTerminal(0); AppendTerminalOutput(0, string.Concat(Enumerable.Range(0, 200).Select(n => $"output {n} {new string('x', 200)}\n"))); }
                else ed.SetText(string.Concat(Enumerable.Range(0, 200).Select(n => $"line {n} {new string('x', 200)}\n")));
                ed.SetSel(0, 0); ed.ScrollCaret();
                Assert(RunButton.Focus(FocusState.Programmatic), "Unable to move focus outside editor");
                var origin = new Windows.Foundation.Point(120, Math.Min(220, control.ActualHeight - 25));
                long caret = ed.CurrentPos, anchor = ed.Anchor, first = ed.FirstVisibleLine;
                gate.Press(1, origin);
                control.Focus(FocusState.Pointer); // Native pointer focus must be rejected before TSF sees it.
                Assert(ed.CurrentPos == caret && ed.Anchor == anchor && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), RunButton), "Touch down moved the caret or acquired text focus");
                gate.Move(1, new Windows.Foundation.Point(origin.X, origin.Y - 100));
                gate.Release(1);
                Assert(ed.FirstVisibleLine > first && ed.CurrentPos == caret && ed.Anchor == anchor, "Swipe failed to scroll or moved the caret");
                Assert(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), RunButton), "Swipe acquired text focus and could summon the keyboard");
                gate.Press(2, origin); gate.Cancel(2); gate.Release(2); // Also stops inertia.
                Assert(ed.CurrentPos == caret && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), RunButton), "Cancelled touch became a tap");
                long expected = TouchPosition(control, origin);
                gate.Press(3, origin); gate.Move(3, new Windows.Foundation.Point(origin.X + 1, origin.Y + 1)); gate.Release(3);
                Assert(ed.CurrentPos == expected && control.FocusState == FocusState.Pointer && ed.Focus && HasEditableNativeInput(), "Confirmed tap did not position the caret and enable native text input");
                long focusedCaret = ed.CurrentPos, focusedAnchor = ed.Anchor;
                nint previousDocument = CurrentNativeTextDocument();
                gate.Press(10, origin);
                nint nextDocument = CurrentNativeTextDocument();
                string documentIdentity = $"previous=0x{previousDocument:X}, next=0x{nextDocument:X}, neutral=0x{gestureInputDocument:X}";
                if (previousDocument != 0) System.Runtime.InteropServices.Marshal.Release(previousDocument);
                if (nextDocument != 0) System.Runtime.InteropServices.Marshal.Release(nextDocument);
                bool hasDocument = HasEditableNativeInput(), blocked = ShouldBlockGestureKeyboard();
                var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot);
                Assert(!ed.Focus && !hasDocument && blocked, $"Already focused editor retained the native input document or allowed the keyboard during touch down: terminal={control == consoles[0]}, nativeFocus={ed.Focus}, document={hasDocument}, {documentIdentity}, blocked={blocked}, gateBlocked={gate.BlockKeyboard()}, xamlFocus={control.FocusState}, focused={focusedElement?.GetType().Name}, self={ReferenceEquals(control, focusedElement)}, owner={ReferenceEquals(control, touchKeyboardGestureOwner)}");
                gate.Move(10, new Windows.Foundation.Point(origin.X + 1, origin.Y - 100)); gate.Release(10);
                Assert(!ed.Focus && !HasEditableNativeInput() && ed.CurrentPos == focusedCaret && ed.Anchor == focusedAnchor, "Swipe reactivated the native input document or moved the caret");
                gate.ResumeInput();
                Assert(ed.Focus && HasEditableNativeInput() && !ShouldBlockGestureKeyboard(), "Hardware keyboard input could not resume after a swipe");
                if (control == consoles[0])
                {
                    tab.Control.Focus(FocusState.Pointer); tab.Control.Editor.Focus = true;
                    gate.Press(11, origin); gate.Move(11, new Windows.Foundation.Point(origin.X, origin.Y - 80)); gate.Release(11);
                    Assert(!HasEditableNativeInput() && !tab.Control.Editor.Focus, "Scrolling another pane retained the previous editor's native input document");
                    touchInputs.GetValue(tab.Control, _ => throw new InvalidOperationException("Touch gate missing")).ResumeInput();
                    Assert(HasEditableNativeInput(), "Hardware input could not resume in the previously focused pane");
                }
                Assert(RunButton.Focus(FocusState.Programmatic), "Unable to move focus before horizontal selection");
                long horizontalOffset = ed.XOffset, verticalOffset = ed.FirstVisibleLine;
                gate.Press(4, origin); gate.Move(4, new Windows.Foundation.Point(origin.X + 90, origin.Y + 2)); gate.Release(4);
                Assert(ed.SelectionStart != ed.SelectionEnd && ed.XOffset == horizontalOffset && ed.FirstVisibleLine == verticalOffset, "Horizontal drag did not select text or scrolled the viewport");
                ed.SetSel(10, 20); long selectedAnchor = ed.Anchor, selectedCaret = ed.CurrentPos;
                double font = control == consoles[0] ? settings.TerminalFontSize : settings.EditorFontSize;
                long startX = ed.XOffset, startY = ed.FirstVisibleLine;
                var other = new Windows.Foundation.Point(origin.X + 100, origin.Y);
                gate.Press(5, origin); gate.Press(6, other);
                gate.Move(5, new Windows.Foundation.Point(origin.X - 40, origin.Y - 60)); gate.Move(6, new Windows.Foundation.Point(other.X - 40, other.Y - 60));
                await Task.Delay(40); gate.Release(5);
                gate.Move(6, new Windows.Foundation.Point(other.X + 50, other.Y)); gate.Release(6);
                Assert(ed.XOffset > startX && ed.FirstVisibleLine > startY && ed.Anchor == selectedAnchor && ed.CurrentPos == selectedCaret, "Two-finger pan failed or changed selection");
                Assert((control == consoles[0] ? settings.TerminalFontSize : settings.EditorFontSize) == font, "Parallel finger motion changed font size");
                gate.Press(7, origin); gate.Move(7, new Windows.Foundation.Point(origin.X + 30, origin.Y));
                gate.Press(8, other);
                gate.Move(7, new Windows.Foundation.Point(origin.X - 20, origin.Y)); gate.Move(8, new Windows.Foundation.Point(other.X + 20, other.Y));
                await Task.Delay(40); gate.Release(7); gate.Release(8);
                Assert(ed.Anchor == selectedAnchor && ed.CurrentPos == selectedCaret, "Pinch retained the first finger's transient text selection");
                Assert((control == consoles[0] ? settings.TerminalFontSize : settings.EditorFontSize) > font && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), RunButton), "Pinch failed to zoom or acquired text focus");
                if (control == consoles[0]) settings.TerminalFontSize = font; else settings.EditorFontSize = font;
                ApplyAppearance();
                await Wait(() => ed.ScrollWidth > control.ActualWidth * control.XamlRoot.RasterizationScale);
                Assert(ed.HScrollBar && (int)ed.WrapMode == 0, "Long lines do not have a horizontal scrollbar");
            }
            }
            finally
            {
                settings.WordWrap = originalWrap; settings.EditorFontSize = originalEditorFont; settings.TerminalFontSize = originalTerminalFont; ApplyAppearance();
                ClearTerminal(0);
                RunButton.Focus(FocusState.Programmatic);
                foreach (var control in new[] { tab.Control, consoles[0] })
                {
                    if (touchInputs.TryGetValue(control, out var gate)) foreach (uint id in Enumerable.Range(1, 11).Select(n => (uint)n)) gate.Cancel(id);
                    control.Editor.SetEmptySelection(0); control.Editor.XOffset = 0; S(control, 2613, 0);
                }
            }
        });
        await Test("folding uses chevrons on headers and vertical scope lines", async () =>
        {
            var ed = tab.Control.Editor;
            ed.SetText("def outer():\n    if True:\n        print('hello')\n    return 1\n\nprint(outer())\n"); ed.Colourise(0, -1);
            await Task.Delay(50);
            UpdateFoldOverlay(tab);
            Assert(S(tab.Control, 2245, 2) == 0, "Native folding markers still share a single gutter column");
            Assert(tab.FoldTargets.Count >= 2, "Nested fold headers are missing");
            var outer = tab.FoldTargets.Single(t => t.Line == 0);
            var inner = tab.FoldTargets.Single(t => t.Line == 1);
            Assert(inner.Bounds.X > outer.Bounds.X + 10, "Nested arrows do not follow indentation");
            ed.SetEmptySelection(0); ed.ScrollCaret();
            RunButton.Focus(FocusState.Programmatic);
            double scale = tab.Control.XamlRoot.RasterizationScale;
            var point = new Windows.Foundation.Point(outer.Bounds.X + outer.Bounds.Width / 2, outer.Bounds.Y + outer.Bounds.Height / 2);
            var gate = touchInputs.GetValue(tab.Control, _ => throw new InvalidOperationException("Touch gate missing"));
            gate.Press(30, point); gate.Release(30);
            Assert(!ed.GetFoldExpanded(0) && !ed.GetLineVisible(1), "Chevron tap did not collapse the scope");
            gate.Press(31, point); gate.Release(31);
            Assert(ed.GetFoldExpanded(0) && ed.GetLineVisible(1), "Chevron tap did not expand the scope");
            Assert(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), RunButton), "Folding acquired text focus");
            tab.Control.Focus(FocusState.Pointer); ed.Focus = true;
            gate.Press(32, point); gate.Release(32);
            Assert(!HasEditableNativeInput() && ShouldBlockGestureKeyboard(), "Folding an already focused editor reactivated the keyboard");
            gate.Press(33, point); gate.Release(33);
            UpdateFoldOverlay(tab);
            var strokes = tab.Folds.Children.OfType<Microsoft.UI.Xaml.Shapes.Line>().Where(l => l.Visibility == Visibility.Visible).ToArray();
            Assert(strokes.Length >= 2 && strokes.Select(l => l.X1).Distinct().Count() >= 2, "Scope guides are not drawn at each indentation level");
            var beforeX = tab.FoldTargets.Single(t => t.Line == 1).Bounds.X;
            ed.XOffset = 25; UpdateFoldOverlay(tab);
            Assert(tab.FoldTargets.Single(t => t.Line == 1).Bounds.X < beforeX, "Inline fold controls do not track horizontal scrolling");
            ed.XOffset = 0; UpdateFoldOverlay(tab);
            await Snapshot(tab.View, "fold-chevrons.png");
            ed.InsertText(0, "# new first line\n"); ed.Colourise(0, -1); await Task.Delay(30); UpdateFoldOverlay(tab);
            Assert(tab.FoldTargets.Any(t => t.Line == 1) && tab.FoldTargets.Any(t => t.Line == 2) && tab.FoldTargets.All(t => t.Line != 0), "Editing left stale fold hit targets or scope ranges");
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
            Assert(consoles[0].Editor.GetText(consoles[0].Editor.Length + 1).Contains("运行成功，退出代码 0"), "Successful exit has no readable result");
            ClearTerminal(0); tab.Control.Editor.SetText("raise RuntimeError('UI_EXPECTED_FAILURE')\n");
            await RunFileAsync(false); FlushOutput();
            Assert(consoles[0].Editor.GetText(consoles[0].Editor.Length + 1).Contains("运行失败：程序或命令未成功完成，退出代码 1"), "Failed exit has no explanation");
            ClearTerminal(0); tab.Control.Editor.SetText("import time\ntime.sleep(60)\n");
            Task stopping = RunFileAsync(false); await Wait(() => runner.IsRunning);
            await ExecuteAsync("stop"); await stopping; FlushOutput();
            string stopped = consoles[0].Editor.GetText(consoles[0].Editor.Length + 1);
            Assert(stopped.Contains("已停止，退出代码 -1") && !stopped.Contains("运行失败"), "User stop was reported as failure");
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
        await Test("incomplete bundled cache is repaired without replacing the original", async () =>
        {
            string data = Path.Combine(directory, "runtime-repair");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RuntimeUpdates.BundledDirectory, "pytouchcode-runtime.json")));
            string version = manifest.RootElement.GetProperty("version").GetString()!;
            string broken = Path.Combine(data, "runtimes", version + "-r" + manifest.RootElement.GetProperty("revision").GetInt32());
            Directory.CreateDirectory(broken);
            File.Copy(Path.Combine(RuntimeUpdates.BundledDirectory, "pytouchcode-runtime.json"), Path.Combine(broken, "pytouchcode-runtime.json"));
            File.Copy(Path.Combine(RuntimeUpdates.BundledDirectory, "python.exe"), Path.Combine(broken, "python.exe"));
            string repaired = (await new RuntimeUpdates(data).EnsureBundledAsync())!;
            Assert(Path.GetDirectoryName(repaired) != broken && File.Exists(Path.Combine(broken, "python.exe")), "Repair replaced the incomplete cache in place");
            Assert(await PythonEnvironments.ProbeAsync(repaired) is not null, "Repaired Python cannot start");
        });
        await Test("default profile runtime loads DLLs and runs the language service", async () =>
        {
            profilePython = await new RuntimeUpdates(AppDataPaths.Directory).EnsureBundledAsync();
            Assert(profilePython is not null && await PythonEnvironments.ProbeAsync(profilePython) is not null, "Default profile Python cannot load its DLLs");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var service = new LanguageService();
            var response = await service.RequestAsync(profilePython!, Bridge, new { action = "diagnose", source = "answer = 42", line = 1, column = 11 }, timeout.Token);
            Assert(response.TryGetProperty("diagnostics", out _), "Default profile language service failed");
            await File.WriteAllTextAsync(Path.Combine(directory, "profile-runtime.json"), JsonSerializer.Serialize(new { directory = AppDataPaths.Directory, python = profilePython, diagnostics = response }, SettingsStore.JsonOptions));
        });
        await Test("native serial discovery is independent of the Python interpreter", async () =>
        {
            var ports = await SerialDevices.DiscoverAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "serial-discovery.json"), JsonSerializer.Serialize(ports, SettingsStore.JsonOptions));
            Assert(ports.All(p => p.Port.StartsWith("COM")), "Serial discovery returned an invalid port");
        });
        await Test("interpreter selector includes MicroPython and serial scan and cancels cleanly", async () =>
        {
            IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
            {
                if (root is T value) yield return value;
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
                    foreach (var child in Elements<T>(VisualTreeHelper.GetChild(root, n))) yield return child;
            }
            string oldTarget = settings.ExecutionTarget, oldPort = settings.DevicePort;
            string? oldBundled = bundled; string oldInterpreter = settings.Interpreter;
            bundled = settings.Interpreter = Path.Combine(directory, "missing-python.exe");
            var opening = EnvironmentDialogAsync();
            ContentDialog? dialog = null;
            await Wait(() => { dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Where(p => p.Child is not null).SelectMany(p => Elements<ContentDialog>(p.Child)).FirstOrDefault(); return dialog is not null; });
            try
            {
                await Wait(() => Elements<ComboBox>(dialog!).Any(c => c.Header as string == "解释器"));
                var selector = Elements<ComboBox>(dialog!).First(c => c.Header as string == "解释器");
                await Wait(() => selector.IsEnabled); selector.SelectedIndex = 1; await Wait(() => selector.IsEnabled);
                Assert(Elements<ComboBox>(dialog!).Any(c => c.Header as string == "USB 串口"), "MicroPython has no port selector");
                Assert(Elements<Button>(dialog!).Any(b => b.Content as string == "扫描串口"), "MicroPython has no scan action");
                var scan = Elements<Button>(dialog!).First(b => b.Content as string == "扫描串口");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(scan);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Wait(() => Elements<TextBlock>(dialog!).Any(t => t.Name == "InterpreterStatus" && (t.Text.StartsWith("发现 ") || t.Text.StartsWith("未发现串口"))));
                var ports = Elements<ComboBox>(dialog!).First(c => c.Header as string == "USB 串口");
                Assert(ports.Items.All(p => p is SerialDevice), "Interpreter serial results are not usable device entries");
                await Snapshot(dialog!, "interpreter-micropython.png");
            }
            finally { dialog!.Hide(); await opening; bundled = oldBundled; settings.Interpreter = oldInterpreter; }
            Assert(settings.ExecutionTarget == oldTarget && settings.DevicePort == oldPort, "Cancel changed the selected interpreter");
            settings.ExecutionTarget = "MicroPython"; settings.DevicePort = "COM65535"; UpdateInterpreter();
            Assert(ViewModel.InterpreterLabel.Contains("MicroPython") && ViewModel.InterpreterLabel.Contains("COM65535"), "Status bar did not show the board target");
            settings.ExecutionTarget = oldTarget; settings.DevicePort = oldPort; UpdateInterpreter();
        });
        await Test("device panel scans native ports when opened", async () =>
        {
            IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
            {
                if (root is T value) yield return value;
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
                    foreach (var child in Elements<T>(VisualTreeHelper.GetChild(root, n))) yield return child;
            }
            string? oldBundled = bundled; string oldInterpreter = settings.Interpreter;
            bundled = settings.Interpreter = Path.Combine(directory, "missing-python.exe");
            var opening = DeviceDialogAsync(); ContentDialog? dialog = null;
            await Wait(() => { dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Where(p => p.Child is not null).SelectMany(p => Elements<ContentDialog>(p.Child)).FirstOrDefault(); return dialog is not null; });
            try
            {
                await Wait(() => Elements<TextBlock>(dialog!).Any(t => t.Name == "DeviceStatus" && (t.Text.StartsWith("发现 ") || t.Text.StartsWith("未发现串口"))));
                var ports = Elements<ComboBox>(dialog!).First(c => c.Header as string == "USB 串口");
                Assert(ports.Items.All(p => p is SerialDevice), "Device panel serial results are not usable device entries");
                await Snapshot(dialog!, "device-serial-scan.png");
            }
            finally { dialog!.Hide(); await opening; bundled = oldBundled; settings.Interpreter = oldInterpreter; }
        });
        await Test("file selection renders stretch and contraction between native animation frames", async () =>
        {
            async Task<int> AccentSpan(string? filename = null)
            {
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(Sidebar);
                byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray(); int min = int.MaxValue, max = -1;
                for (int n = 0; n < pixels.Length; n += 4)
                    if (pixels[n] == 0x53 && pixels[n + 1] == 0x31 && pixels[n + 2] == 0xB7 && pixels[n + 3] == 255)
                    { int row = n / 4 / bitmap.PixelWidth; min = Math.Min(min, row); max = Math.Max(max, row); }
                if (filename is not null)
                {
                    using var file = File.Create(Path.Combine(directory, filename)); using var stream = file.AsRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 192, 192, pixels); await encoder.FlushAsync();
                }
                return max < 0 ? 0 : max - min + 1;
            }
            bool previous = settings.UseSystemAccent; string accent = settings.Accent;
            settings.UseSystemAccent = false; settings.Accent = "#B73153"; ApplyAppearance();
            settings.Workspace = directory; await RefreshFilesAsync();
            var root = FileTree.RootNodes[0]; FileTree.SelectedNode = root;
            await Task.Delay(150); await Snapshot(Sidebar, "navigation-sidebar-debug.png");
            Assert(FilesSidebarPanel.Visibility == Visibility.Visible && FileTreeHost.ActualHeight > 30, $"Sidebar content is hidden or empty: files={FilesSidebarPanel.Visibility}, debug={DebugSidebarPanel.Visibility}, host={FileTreeHost.ActualWidth}x{FileTreeHost.ActualHeight}, nav={SidebarNavigation.ActualWidth}x{SidebarNavigation.ActualHeight}, selected={((SidebarNavigation.SelectedItem as NavigationViewItem)?.Tag)}");
            await Wait(() => FileSelectionIndicator.Opacity == 1 && indicatorY.HasValue); await Task.Delay(650);
            float first = indicatorY!.Value; int baseline = await AccentSpan();
            FileTree.SelectedNode = root.Children[Math.Min(3, root.Children.Count - 1)];
            await Wait(() => indicatorY.HasValue && indicatorY.Value != first); await Task.Delay(100);
            int stretched = await AccentSpan("file-selection-moving.png");
            await Task.Delay(650); int settled = await AccentSpan("file-selection.png");
            bool animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            await File.WriteAllTextAsync(Path.Combine(directory, "selection-animation.json"), JsonSerializer.Serialize(new { animations, baseline, stretched, settled }));
            Assert(baseline > 0 && settled > 0, "Selection indicator did not render");
            if (animations) Assert(stretched > baseline + 3 && settled <= baseline + 2, $"Indicator did not stretch and contract: start={baseline}, moving={stretched}, end={settled}");
            Assert(FileSelectionIndicator.Width == 3 && FileSelectionIndicator.Height == 16, "Selection indicator dimensions are incorrect");
            settings.UseSystemAccent = previous; settings.Accent = accent; ApplyAppearance();
        });
        await Test("native navigation switches sidebar and settings without losing staged values", async () =>
        {
            SidebarNavigation.SelectedItem = SidebarNavigation.MenuItems[1];
            await Wait(() => DebugSidebarPanel.Visibility == Visibility.Visible && FilesSidebarPanel.Visibility == Visibility.Collapsed);
            SidebarNavigation.SelectedItem = SidebarNavigation.MenuItems[0];
            await Wait(() => FilesSidebarPanel.Visibility == Visibility.Visible && DebugSidebarPanel.Visibility == Visibility.Collapsed);
            await Task.Delay(650);
            var refresh = RefreshFilesButton.TransformToVisual(Sidebar).TransformPoint(new Windows.Foundation.Point());
            Assert(refresh.X + RefreshFilesButton.ActualWidth <= Sidebar.ActualWidth + 1, $"Navigation content clipped the refresh button: x={refresh.X}, w={RefreshFilesButton.ActualWidth}, sidebar={Sidebar.ActualWidth}, nav={SidebarNavigation.ActualWidth}, page={SidebarPages.ActualWidth}");
            Assert(InterpreterButton.TransformToVisual(StatusBar).TransformPoint(new Windows.Foundation.Point()).X < 24, "Interpreter selector is not at the left of the status bar");
            IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
            {
                if (root is T value) yield return value;
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
                    foreach (var child in Elements<T>(VisualTreeHelper.GetChild(root, n))) yield return child;
            }
            string originalTheme = settings.Theme; double originalTransparency = settings.MaterialTransparency; bool originalPairs = settings.AutoCloseBrackets;
            var opening = SettingsDialogAsync(); ContentDialog? dialog = null;
            await Wait(() => { dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Where(p => p.Child is not null).SelectMany(p => Elements<ContentDialog>(p.Child)).FirstOrDefault(); return dialog is not null; });
            try
            {
                await Wait(() => Elements<NavigationView>(dialog!).Any());
                var navigation = Elements<NavigationView>(dialog!).First();
                Assert(navigation.MenuItems.Count == 4, "Settings navigation categories are incomplete");
                var transparency = Elements<Slider>(dialog!).First(s => s.Name == "MaterialTransparency");
                transparency.Value = 83;
                Assert(settings.MaterialTransparency == originalTransparency, "Preview prematurely saved transparency");
                await Wait(() => Elements<ComboBox>(dialog!).Any(c => c.Header as string == "应用主题"));
                var theme = Elements<ComboBox>(dialog!).First(c => c.Header as string == "应用主题"); theme.SelectedItem = "Dark";
                foreach (var item in navigation.MenuItems.Cast<NavigationViewItem>().Skip(1))
                {
                    navigation.SelectedItem = item; await Wait(() => ReferenceEquals(((ContentControl)navigation.Tag).Content, item.Tag));
                    if (item.Content as string == "编辑器")
                    {
                        await Wait(() => Elements<CheckBox>(dialog!).Any(c => c.Content as string == "自动补全括号和引号"));
                        var pairs = Elements<CheckBox>(dialog!).First(c => c.Content as string == "自动补全括号和引号");
                        Assert(pairs.IsChecked == originalPairs, "Pair setting has the wrong initial value");
                        pairs.IsChecked = !originalPairs;
                    }
                }
                navigation.SelectedItem = navigation.MenuItems[0];
                await Wait(() => ReferenceEquals(((ContentControl)navigation.Tag).Content, ((NavigationViewItem)navigation.MenuItems[0]).Tag));
                Assert(theme.SelectedItem as string == "Dark", "Navigation discarded staged settings");
                await Task.Delay(650); await Snapshot(dialog!, "native-settings-navigation.png");
            }
            finally { dialog!.Hide(); await opening; }
            Assert(settings.Theme == originalTheme, "Cancel saved staged settings");
            Assert(settings.AutoCloseBrackets == originalPairs, "Cancel saved bracket pairing changes");
            Assert(settings.MaterialTransparency == originalTransparency && (SystemBackdrop is not AdjustableBackdrop backdrop || backdrop.Transparency == originalTransparency), "Cancel did not restore material transparency");
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
        var commandArgs = Environment.GetCommandLineArgs(); int boardIndex = Array.IndexOf(commandArgs, "--pyboard-test");
        if (boardIndex >= 0 && boardIndex + 1 < commandArgs.Length)
            await Test("physical Pyboard first run, continuous execution, stop, rerun, errors and inline REPL", async () =>
            {
                string port = SerialDevices.ValidatePort(commandArgs[boardIndex + 1]);
                string? oldBundled = bundled; string oldInterpreter = settings.Interpreter, oldTarget = settings.ExecutionTarget, oldPort = settings.DevicePort, oldWorkspace = settings.Workspace;
                var oldTab = Tabs.SelectedItem;
                bundled = profilePython ?? await new RuntimeUpdates(AppDataPaths.Directory).EnsureBundledAsync();
                const string identify = "import os,sys,json,uhashlib,ubinascii;print(json.dumps({'machine':str(os.uname()),'platform':sys.platform,'hashes':{n:ubinascii.hexlify(uhashlib.sha256(open(n,'rb').read()).digest()).decode() for n in ('boot.py','main.py')}}))";
                try
                {
                    string before = (await MpRemoteAsync(port, "resume", "exec", identify)).Trim();
                    using var info = JsonDocument.Parse(before);
                    Assert(info.RootElement.GetProperty("platform").GetString() == "pyboard", "Selected device is not a Pyboard");
                    settings.ExecutionTarget = "MicroPython"; settings.DevicePort = port;
                    async Task Cancelled(Task task)
                    {
                        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Stopped task did not report cancellation"); }
                        catch (OperationCanceledException) { }
                    }
                    // Opt-in hardware test: run the user's saved board script
                    // from its USB volume without saving or changing either file.
                    string? boardRoot = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                        .Select(d => d.RootDirectory.FullName).FirstOrDefault(d => File.Exists(Path.Combine(d, "pybcdc.inf")) && File.Exists(Path.Combine(d, "main.py")));
                    bool usbScriptTested = false;
                    if (boardRoot is not null)
                    {
                        settings.Workspace = boardRoot;
                        await OpenDocumentAsync(Path.Combine(boardRoot, "main.py"));
                        Notice.IsOpen = false;
                        Task firstRun = ExecuteAsync("run");
                        await Wait(() => device.IsRunning && ViewModel.IsRunning);
                        await Task.Delay(2500);
                        Assert(!firstRun.IsCompleted && !Notice.IsOpen, "First run of the USB board script failed");
                        Assert(ViewModel.SessionStatus.StartsWith("正在运行 main.py"), "Device running status is missing");
                        await ExecuteAsync("stop"); await Cancelled(firstRun);
                        Assert(!Notice.IsOpen && !ViewModel.IsRunning, "Stopping a board program was reported as an error");
                        await StopDeviceAsync(); usbScriptTested = true;
                    }
                    string continuous = Path.Combine(directory, "pyboard-continuous-probe.py");
                    await File.WriteAllTextAsync(continuous, "import pyb\nprint('PTC_CONTINUOUS_READY')\nwhile True:\n    pyb.delay(100)\n");
                    await OpenDocumentAsync(continuous); ClearTerminal(3); Notice.IsOpen = false;
                    Task continuousRun = ExecuteAsync("run");
                    await Wait(() => { FlushOutput(); return consoles[3].Editor.GetText(consoles[3].Editor.Length + 1).Contains("PTC_CONTINUOUS_READY"); });
                    await ExecuteAsync("run");
                    Assert(!continuousRun.IsCompleted && device.IsRunning && deviceCommand?.IsCancellationRequested == false, "Duplicate Run killed the active program");
                    await Task.Delay(700);
                    Notice.IsOpen = false;
                    ApplyDiagnostics(Active!, JsonSerializer.SerializeToElement(new { diagnostics = Array.Empty<object>() }));
                    Assert(ViewModel.SessionStatus.StartsWith("正在运行"), "Diagnostics overwrote device running status");
                    SelectChannel(1); // Stop must follow the device task, not the visible console.
                    await ExecuteAsync("stop"); await Cancelled(continuousRun);
                    FlushOutput();
                    string stoppedOutput = consoles[3].Editor.GetText(consoles[3].Editor.Length + 1);
                    Assert(!Notice.IsOpen && stoppedOutput.Contains("已停止，退出代码 -1") && !stoppedOutput.Contains("运行失败"), "Intentional stop appeared as an mpremote failure");
                    ClearTerminal(3);
                    Task rerun = ExecuteAsync("rerun");
                    await Wait(() => { FlushOutput(); return consoles[3].Editor.GetText(consoles[3].Editor.Length + 1).Contains("PTC_CONTINUOUS_READY"); });
                    Assert(!rerun.IsCompleted && ViewModel.IsRunning, "Rerun did not start a persistent program");
                    await InterruptDeviceAsync(); await Cancelled(rerun); await StopDeviceAsync();
                    string silent = Path.Combine(directory, "pyboard-silent-probe.py");
                    await File.WriteAllTextAsync(silent, "import pyb\nwhile True:\n    pyb.delay(100)\n");
                    await OpenDocumentAsync(silent);
                    Task lostProcess = ExecuteAsync("run");
                    await Wait(() => device.IsRunning); await Task.Delay(700);
                    device.Stop(); // Simulate an unexpected host process loss.
                    try { await lostProcess.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Unexpected process exit was ignored"); }
                    catch (IOException e) { Assert(e.Message.Contains("进程被终止") && e.Message.Contains("代码 -1") && e.Message.Contains("0xFFFFFFFF"), "Empty process failure still has no exit explanation"); }
                    await InterruptDeviceAsync(); await StopDeviceAsync();
                    try { await MpRemoteAsync(port, "exec", "raise RuntimeError('PTC_EXPECTED_FAILURE')"); throw new InvalidOperationException("Device exception was ignored"); }
                    catch (IOException e) { Assert(e.Message.Contains("PTC_EXPECTED_FAILURE"), "Device exception details were lost"); }
                    string script = Path.Combine(directory, "pyboard-ram-probe.py");
                    await File.WriteAllTextAsync(script, "print('PTC_RAM_PROBE', 6 * 7)\n");
                    string run = await MpRemoteAsync(port, "resume", "run", script);
                    Assert(run.Contains("PTC_RAM_PROBE 42"), "RAM execution did not return the expected result");
                    ClearTerminal(3); await ConnectDeviceAsync(port); await Wait(() => device.IsRunning);
                    await device.SendAsync(JsonSerializer.Serialize(new { control = 3 }));
                    await Wait(() => { FlushOutput(); return consoles[3].Editor.GetText(consoles[3].Editor.Length + 1).Contains(">>>"); });
                    ReplaceTerminalDraft(3, "print('PTC_REPL_PROBE', 6 * 7)"); await SubmitTerminalAsync(3);
                    await Wait(() => { FlushOutput(); return consoles[3].Editor.GetText(consoles[3].Editor.Length + 1).Contains("PTC_REPL_PROBE 42"); });
                    string transcript = consoles[3].Editor.GetText(consoles[3].Editor.Length + 1);
                    await StopDeviceAsync();
                    string after = (await MpRemoteAsync(port, "resume", "exec", identify)).Trim();
                    Assert(before == after, "Board identification or boot.py/main.py hashes changed");
                    await File.WriteAllTextAsync(Path.Combine(directory, "pyboard-hardware.json"), JsonSerializer.Serialize(new { port, python = bundled, identification = info.RootElement, usbScriptTested, continuousRun = true, duplicateRunPreserved = true, stopAndRerun = true, errorDetails = true, ram = run, repl = transcript, filesUnchanged = true }, SettingsStore.JsonOptions));
                }
                finally
                {
                    await StopDeviceAsync(); bundled = oldBundled;
                    settings.Interpreter = oldInterpreter; settings.ExecutionTarget = oldTarget; settings.DevicePort = oldPort; settings.Workspace = oldWorkspace;
                    selectedPort = oldPort; Tabs.SelectedItem = oldTab; UpdateInterpreter();
                }
            });
        await File.WriteAllTextAsync(Path.Combine(directory, "ui-smoke.json"), JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, viewport = new { width = Root.ActualWidth, height = Root.ActualHeight }, results }, SettingsStore.JsonOptions));
        runner.Stop(); shell.Stop(); repl.Stop(); device.Stop(); language.Stop(); closing = true; Close();
    }
}

