using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PyTouchCode.Core;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Windows.UI;
using WinUIEditor;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    [DllImport("WinUIEditor.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    static extern nint CreateLexer(string name);
    static long S(EditorBaseControl control, int message, ulong w = 0, long l = 0) => control.SendMessage((ScintillaMessage)message, w, l);
    static int ColorRef(string hex) { var c = ParseColor(hex); return c.R | c.G << 8 | c.B << 16; }
    static Color ParseColor(string text)
    {
        text = text.TrimStart('#'); if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int color)) throw new InvalidDataException("颜色应为 #RRGGBB。");
        return Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
    static string MixColor(string background, string foreground, double amount)
    {
        var a = ParseColor(background); var b = ParseColor(foreground);
        return $"#{(int)(a.R + (b.R - a.R) * amount):X2}{(int)(a.G + (b.G - a.G) * amount):X2}{(int)(a.B + (b.B - a.B) * amount):X2}";
    }
    void ConfigureEditor(EditorBaseControl control, bool terminal)
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var ed = control.Editor;
        if (!terminal && S(control, 4002) != 2)
        {
            // WinUIEdit's language convenience property does not configure Python.
            // Its bundled Lexilla exports the full native Python lexer.
            nint lexer = CreateLexer("python");
            if (lexer == 0) throw new InvalidOperationException("原生 Python 语法组件不可用。");
            S(control, 4033, 0, lexer.ToInt64()); // Scintilla owns and releases ILexer5.
        }
        var palette = terminal ? ColorSchemes.ForTerminal(settings, dark) : ColorSchemes.ForEditor(settings, dark);
        string bg = palette.Background, fg = palette.Foreground;
        ed.StyleSetFont(32, terminal ? settings.TerminalFont : settings.EditorFont);
        S(control, 2061, 32, (long)((terminal ? settings.TerminalFontSize : settings.EditorFontSize) * 100));
        ed.StyleSetWeight(32, (WinUIEditor.FontWeight)(terminal ? settings.TerminalFontWeight : settings.EditorFontWeight));
        ed.StyleSetBack(32, ColorRef(bg)); ed.StyleSetFore(32, ColorRef(fg)); ed.StyleClearAll();
        int[] styles = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 15];
        string[] colors = [palette.BrightBlack, palette.Yellow, palette.Green, palette.Green, palette.Purple, palette.Green, palette.Green, palette.Yellow, palette.Blue, palette.Cyan, palette.BrightBlack, palette.Red, palette.Yellow];
        for (int i = 0; i < styles.Length; i++) ed.StyleSetFore(styles[i], ColorRef(colors[i]));
        ed.StyleSetFore(33, ColorRef(palette.BrightBlack)); ed.StyleSetBack(33, ColorRef(bg));
        ed.SetFoldMarginColour(true, ColorRef(bg)); ed.SetFoldMarginHiColour(true, ColorRef(bg));
        for (int margin = 0; margin < 5; margin++) ed.SetMarginBackN(margin, ColorRef(bg));
        S(control, 2069, (ulong)ColorRef(palette.CursorColor)); S(control, 2068, 1, ColorRef(palette.SelectionBackground ?? MixColor(bg, fg, .23)));
        ed.CaretLineBack = ColorRef(MixColor(bg, fg, .045));
        ed.CaretLineVisible = !terminal;
        ed.CaretLineFrame = 0; // No frame around the current line.
        S(control, 2132, 0); // No indentation guides inherited from control defaults.
        S(control, 2363, 0); // No edge-column rule.
        S(control, 2021, 0); // No whitespace background markers.
        S(control, 2037, 65001); S(control, 2242, 0, terminal ? 0 : 52); S(control, 2240, 0, 1);
        S(control, 2242, 1, terminal ? 0 : 24); S(control, 2240, 1, 0); S(control, 2244, 1, 1); S(control, 2246, 1, 1);
        S(control, 2242, 2, terminal ? 0 : (int)Math.Ceiling(20 * (control.XamlRoot?.RasterizationScale ?? 1))); S(control, 2240, 2, 0); S(control, 2244, 2, 0); S(control, 2246, 2, 0);
        S(control, 2040, 0, 0); S(control, 2042, 0, ColorRef("#E06C75")); S(control, 2040, 1, 22); S(control, 2042, 1, ColorRef(dark ? "#514A2D" : "#FFF0C2"));
        ed.TabWidth = settings.IndentSize; ed.UseTabs = settings.UseTabs; S(control, 2122, (ulong)settings.IndentSize);
        ed.WrapMode = (Wrap)(settings.WordWrap ? 1 : 0);
        ed.HScrollBar = true; ed.ScrollWidthTracking = true;
        S(control, 2525, 0); S(control, 2527, 0);
        double lineHeight = terminal ? settings.TerminalLineHeight : settings.EditorLineHeight;
        int extra = (int)Math.Round(ed.TextHeight(0) * (lineHeight - 1));
        S(control, 2525, (ulong)(extra / 2)); S(control, 2527, (ulong)(extra - extra / 2));
        S(control, 2260, 3); S(control, 2262, 1); // Native multiline Tab / Backtab indentation.
        S(control, 2106, 10); S(control, 2210, 10); S(control, 2208, 64); S(control, 2115, 1);
        S(control, 2264, 650); // Hover delay; no polling.
        S(control, 2080, 8, 1); S(control, 2082, 8, ColorRef("#E06C75")); S(control, 2080, 9, 7); S(control, 2082, 9, ColorRef("#61AFEF"));
        ed.SetProperty("fold", "1"); ed.SetProperty("fold.compact", "1"); S(control, 2663, 7);
        if (terminal)
        {
            for (ulong margin = 0; margin < 5; margin++) S(control, 2242, margin, 0);
            S(control, 2012, 0); // Output history cannot be changed through undo.
            S(control, 2155, 0, 8); S(control, 2157, 0, 8);
        }
        if (!terminal) ed.SetKeyWords(0, "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield");
    }
    bool BackspaceIndent(EditorBaseControl control)
    {
        var ed = control.Editor;
        if (ed.ReadOnly || ed.Selections != 1 || ed.SelectionStart != ed.SelectionEnd) return false;
        long caret = ed.CurrentPos, line = ed.LineFromPosition(caret), start = ed.PositionFromLine(line);
        if (caret <= start) return false;
        string text = ed.GetLine(line);
        int prefix = (int)(caret - start);
        // Only ASCII indentation precedes the caret, so character and UTF-8 offsets agree.
        if (prefix > text.Length || text.Take(prefix).Any(c => c is not (' ' or '\t'))) return false;
        int column = (int)ed.GetColumn(caret), step = Math.Max(1, settings.IndentSize);
        int targetColumn = (column - 1) / step * step;
        int offset = 0, width = 0;
        while (offset < prefix)
        {
            int next = text[offset] == '\t' ? (width / ed.TabWidth + 1) * ed.TabWidth : width + 1;
            if (next > targetColumn) break;
            width = next; offset++;
        }
        // Native unindent uses the whole line and moves to its new indent end.
        // Delete only the prefix before the caret, including when it sits inside an indent.
        ed.BeginUndoAction();
        try { ed.SetSel(start + offset, caret); ed.ReplaceSel(""); }
        finally { ed.EndUndoAction(); }
        ed.ScrollCaret();
        return true;
    }
    void AutoPairAndIndent(EditorTab tab, int character)
    {
        if (tab.Loading) return;
        var ed = tab.Control.Editor;
        long pos = ed.CurrentPos; long line = ed.LineFromPosition(pos);
        if (character == '\n' && line > 0)
        {
            string previous = ed.GetLine(line - 1).TrimEnd('\r', '\n');
            int spaces = previous.TakeWhile(char.IsWhiteSpace).Sum(c => c == '\t' ? settings.IndentSize : 1);
            if (previous.TrimEnd().EndsWith(':')) spaces += settings.IndentSize;
            if (spaces > 0 && string.IsNullOrWhiteSpace(ed.GetLine(line)))
            {
                string indent = settings.UseTabs ? new string('\t', spaces / settings.IndentSize) + new string(' ', spaces % settings.IndentSize) : new string(' ', spaces);
                if (ed.GetLine(line).TrimEnd('\r', '\n').Length == 0) ed.ReplaceSel(indent);
            }
        }
        else if (character is '(' or '[' or '{' or '\'' or '"')
        {
            // Avoid pairing inside comments/strings or before an identifier.
            int style = ed.GetStyleAt(Math.Max(0, pos - 2));
            if (style is 1 or 3 or 4 or 6 or 7 or 12) return;
            int next = ed.GetCharAt(pos);
            if (next != 0 && next != '\r' && next != '\n' && next != ' ' && next != ')' && next != ']' && next != '}') return;
            char close = character switch { '(' => ')', '[' => ']', '{' => '}', _ => (char)character };
            ed.InsertText(pos, close.ToString()); ed.SetEmptySelection(pos);
        }
    }
    static (string Source, string Before, int Line, int Column) Snapshot(EditorTab tab)
    {
        var ed = tab.Control.Editor; string source = ed.GetText(ed.Length + 1);
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        string before = Encoding.UTF8.GetString(bytes, 0, (int)Math.Clamp(ed.CurrentPos, 0, bytes.Length));
        int line = before.Count(c => c == '\n') + 1;
        string current = before[(before.LastIndexOf('\n') + 1)..];
        // Jedi counts Unicode codepoints, whereas .NET strings use UTF-16 units.
        int column = current.EnumerateRunes().Count();
        return (source, before, line, column);
    }
    async Task CompleteAsync(EditorTab tab, bool explicitRequest)
    {
        if (Active != tab || !File.Exists(settings.Interpreter) || tab.Control.Editor.Length > 2_000_000) return;
        tab.CompletionCancellation?.Cancel(); var cancellation = new CancellationTokenSource(); tab.CompletionCancellation = cancellation;
        var snap = Snapshot(tab); int revision = tab.Revision; long position = tab.Control.Editor.CurrentPos;
        var result = await language.RequestAsync(settings.Interpreter, Bridge, new { action = "complete", source = snap.Source, path = tab.Document.Path, line = snap.Line, column = snap.Column }, cancellation.Token, settings.Environment);
        if (revision != tab.Revision || Active != tab || tab.Control.Editor.CurrentPos != position) return;
        ApplyDiagnostics(tab, result);
        if (result.TryGetProperty("error", out var error)) { if (explicitRequest) Notify(error.GetString()!); return; }
        if (result.TryGetProperty("signatures", out var signatures) && signatures.GetArrayLength() > 0) tab.Control.Editor.CallTipShow(position, signatures[0].GetString()!);
        if (!result.TryGetProperty("completions", out var items) || items.GetArrayLength() == 0) return;
        string prefix = System.Text.RegularExpressions.Regex.Match(snap.Before, @"[\p{L}\p{N}_]*$").Value;
        if (explicitRequest || snap.Before.EndsWith('.'))
        {
            tab.Ghost.Visibility = Visibility.Collapsed; tab.GhostInsertion = "";
            tab.Control.Editor.AutoCShow(Encoding.UTF8.GetByteCount(prefix), string.Join('\n', items.EnumerateArray().Select(x => x.GetProperty("name").GetString())));
        }
        else if (settings.GhostText && prefix.Length >= 2 && !tab.Control.Editor.AutoCActive())
        {
            string insertion = items[0].GetProperty("insert").GetString() ?? "";
            if (insertion.Length == 0) return;
            tab.GhostInsertion = insertion; tab.Ghost.Text = insertion; tab.Ghost.FontFamily = new FontFamily(settings.EditorFont); tab.Ghost.FontSize = settings.EditorFontSize;
            double scale = tab.Control.XamlRoot.RasterizationScale;
            tab.Ghost.Margin = new Thickness(tab.Control.Editor.PointXFromPosition(position) / scale, tab.Control.Editor.PointYFromPosition(position) / scale, 0, 0);
            tab.Ghost.Visibility = Visibility.Visible;
        }
    }
    void ApplyDiagnostics(EditorTab tab, JsonElement result)
    {
        var ed = tab.Control.Editor; ed.IndicatorCurrent = 8; ed.IndicatorClearRange(0, ed.Length);
        if (!result.TryGetProperty("diagnostics", out var list)) return;
        foreach (var diagnostic in list.EnumerateArray())
        {
            long line = Math.Clamp(diagnostic.GetProperty("line").GetInt64() - 1, 0, ed.LineCount - 1);
            long start = ed.PositionFromLine(line), end = ed.GetLineEndPosition(line);
            ed.IndicatorFillRange(start, Math.Max(1, end - start));
        }
        if (ViewModel.IsRunning || deviceCommand is not null) return;
        if (list.GetArrayLength() > 0) ViewModel.SessionStatus = $"第 {list[0].GetProperty("line")} 行 {list[0].GetProperty("message").GetString()}";
        else if (!runner.IsRunning) ViewModel.SessionStatus = "语法检查通过";
    }
    async Task HoverAsync(EditorTab tab, int position)
    {
        if (position < 0 || !settings.Completion || !File.Exists(settings.Interpreter) || Active != tab) return;
        var ed = tab.Control.Editor;
        long line = ed.LineFromPosition(position); string text = ed.GetLine(line);
        int count = (int)Math.Clamp(position - ed.PositionFromLine(line), 0, Encoding.UTF8.GetByteCount(text));
        int column = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text), 0, count).EnumerateRunes().Count();
        int revision = tab.Revision;
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await language.RequestAsync(settings.Interpreter, Bridge, new { action = "hover", source = ed.GetText(ed.Length + 1), path = tab.Document.Path, line = line + 1, column }, ct.Token, settings.Environment);
        if (revision == tab.Revision && Active == tab && result.TryGetProperty("doc", out var doc) && !string.IsNullOrEmpty(doc.GetString())) ed.CallTipShow(position, doc.GetString()![..Math.Min(1800, doc.GetString()!.Length)]);
    }
    async Task FormatAsync(EditorTab tab)
    {
        if (!File.Exists(settings.Interpreter)) { Notify("请先选择 Python 环境。"); return; }
        var ed = tab.Control.Editor; int revision = tab.Revision;
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var snap = Snapshot(tab);
        var result = await language.RequestAsync(settings.Interpreter, Bridge, new { action = "format", source = snap.Source, path = tab.Document.Path }, ct.Token, settings.Environment);
        if (revision != tab.Revision) { Notify("格式化期间文档已改变，请重试。"); return; }
        if (result.TryGetProperty("formatted", out var formatted) && formatted.GetString() is { } value && value != snap.Source)
        { ed.BeginUndoAction(); ed.TargetStart = 0; ed.TargetEnd = ed.Length; ed.ReplaceTarget(Encoding.UTF8.GetByteCount(value), value); ed.EndUndoAction(); }
        ApplyDiagnostics(tab, result);
    }
    void ToggleComment(EditorTab tab)
    {
        var ed = tab.Control.Editor; long first = ed.LineFromPosition(ed.SelectionStart), last = ed.LineFromPosition(ed.SelectionEnd);
        if (last > first && ed.SelectionEnd == ed.PositionFromLine(last)) last--;
        bool uncomment = Enumerable.Range((int)first, (int)(last - first + 1)).All(i => string.IsNullOrWhiteSpace(ed.GetLine(i)) || ed.GetLine(i).TrimStart().StartsWith('#'));
        ed.BeginUndoAction();
        for (long line = last; line >= first; line--)
        {
            string value = ed.GetLine(line); int indent = value.TakeWhile(c => c is ' ' or '\t').Count();
            if (string.IsNullOrWhiteSpace(value)) continue;
            long start = ed.PositionFromLine(line) + indent;
            if (uncomment) ed.DeleteRange(start, value.Length > indent + 1 && value[indent + 1] == ' ' ? 2 : 1);
            else ed.InsertText(start, "# ");
        }
        ed.EndUndoAction();
    }
    async Task FindDialogAsync(EditorTab tab)
    {
        var find = new TextBox { Header = "查找", Text = tab.Control.Editor.GetSelText() };
        var replace = new TextBox { Header = "替换为" };
        var match = new CheckBox { Content = "区分大小写" }; var message = new TextBlock();
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(find); panel.Children.Add(replace); panel.Children.Add(match);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var next = new Button { Content = "查找下一个" }; var one = new Button { Content = "替换" }; var all = new Button { Content = "全部替换" };
        actions.Children.Add(next); actions.Children.Add(one); actions.Children.Add(all); panel.Children.Add(actions); panel.Children.Add(message);
        bool Search()
        {
            var ed = tab.Control.Editor; if (find.Text.Length == 0) return false;
            ed.SearchFlags = (FindOption)(match.IsChecked == true ? 4 : 0); ed.TargetStart = ed.SelectionEnd; ed.TargetEnd = ed.Length;
            long pos = ed.SearchInTarget(Encoding.UTF8.GetByteCount(find.Text), find.Text);
            if (pos < 0) { ed.TargetStart = 0; ed.TargetEnd = ed.Length; pos = ed.SearchInTarget(Encoding.UTF8.GetByteCount(find.Text), find.Text); }
            if (pos < 0) { message.Text = "没有找到匹配项"; return false; }
            ed.SetSel(ed.TargetStart, ed.TargetEnd); ed.ScrollCaret(); message.Text = "已定位匹配项"; return true;
        }
        next.Click += (_, _) => Search();
        one.Click += (_, _) => { var ed = tab.Control.Editor; if (find.Text.Length == 0) return; if (string.Equals(ed.GetSelText(), find.Text, match.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) ed.ReplaceSel(replace.Text); Search(); };
        all.Click += (_, _) =>
        {
            if (find.Text.Length == 0) return; var ed = tab.Control.Editor; ed.BeginUndoAction(); int count = 0; long start = 0;
            ed.SearchFlags = (FindOption)(match.IsChecked == true ? 4 : 0);
            while (true) { ed.TargetStart = start; ed.TargetEnd = ed.Length; if (ed.SearchInTarget(Encoding.UTF8.GetByteCount(find.Text), find.Text) < 0) break; start = ed.TargetStart + ed.ReplaceTarget(Encoding.UTF8.GetByteCount(replace.Text), replace.Text); count++; }
            ed.EndUndoAction(); message.Text = $"已替换 {count} 处";
        };
        await ShowDialogAsync(new ContentDialog { Title = "查找与替换", Content = panel, CloseButtonText = "完成" });
    }
}


