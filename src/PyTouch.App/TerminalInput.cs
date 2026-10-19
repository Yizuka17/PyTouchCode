using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using WinUIEditor;
using System.Text;

namespace PyTouch.App;
public sealed partial class MainWindow
{
    sealed class TerminalState
    {
        public long InputStart;
        public bool Changing, Sending;
        public List<string> History = [];
        public int HistoryIndex;
        public string Draft = "";
    }
    readonly TerminalState[] terminalStates = Enumerable.Range(0, 4).Select(_ => new TerminalState()).ToArray();
    static bool KeyHeld(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    string TerminalDraft(int channel)
    {
        var ed = consoles[channel].Editor;
        ed.TargetStart = terminalStates[channel].InputStart; ed.TargetEnd = ed.Length;
        return ed.GetTargetText();
    }
    void ProtectTerminal(int channel)
    {
        var state = terminalStates[channel];
        if (!state.Changing) consoles[channel].Editor.ReadOnly = consoles[channel].Editor.SelectionStart < state.InputStart;
    }
    void AttachTerminal(EditorBaseControl console, int channel)
    {
        console.Editor.UpdateUI += (_, _) => ProtectTerminal(channel);
        console.PreviewKeyDown += async (_, e) => await SafeAsync(() => TerminalKeyAsync(channel, e));
    }
    void ReplaceTerminalDraft(int channel, string text)
    {
        var state = terminalStates[channel]; var ed = consoles[channel].Editor;
        state.Changing = true;
        try
        {
            ed.ReadOnly = false; ed.SetSel(state.InputStart, ed.Length); ed.ReplaceSel(text); ed.GotoPos(ed.Length);
        }
        finally { state.Changing = false; ProtectTerminal(channel); }
    }
    void AppendTerminalOutput(int channel, string text)
    {
        var state = terminalStates[channel]; var ed = consoles[channel].Editor;
        long start = state.InputStart, bytes = Encoding.UTF8.GetByteCount(text);
        long anchor = ed.Anchor, caret = ed.CurrentPos;
        bool following = caret >= start;
        state.Changing = true;
        try
        {
            ed.ReadOnly = false; ed.InsertText(start, text); state.InputStart += bytes;
            long removed = 0;
            if (state.InputStart > 1_000_000)
            {
                removed = ed.PositionFromLine(ed.LineFromPosition(state.InputStart - 800_000) + 1);
                ed.DeleteRange(0, removed); state.InputStart -= removed;
            }
            ed.SetSel(Math.Max(0, anchor + (anchor >= start ? bytes : 0) - removed), Math.Max(0, caret + (caret >= start ? bytes : 0) - removed));
            if (following) ed.ScrollCaret();
        }
        finally { state.Changing = false; ProtectTerminal(channel); }
    }
    void ClearTerminal(int channel)
    {
        string draft = TerminalDraft(channel);
        var state = terminalStates[channel]; state.Changing = true;
        try { var ed = consoles[channel].Editor; ed.ReadOnly = false; ed.ClearAll(); state.InputStart = 0; }
        finally { state.Changing = false; }
        ReplaceTerminalDraft(channel, draft);
    }
    void MoveTerminalHistory(int channel, int direction)
    {
        var state = terminalStates[channel];
        if (state.HistoryIndex == state.History.Count) state.Draft = TerminalDraft(channel);
        state.HistoryIndex = Math.Clamp(state.HistoryIndex + direction, 0, state.History.Count);
        ReplaceTerminalDraft(channel, state.HistoryIndex == state.History.Count ? state.Draft : state.History[state.HistoryIndex]);
    }
    async Task SubmitTerminalAsync(int channel)
    {
        var state = terminalStates[channel]; if (state.Sending) return;
        var session = new[] { runner, shell, repl, device }[channel];
        if (!session.IsRunning) throw new InvalidOperationException("请先启动此终端，或运行需要输入的 Python 程序。");
        if (channel == 3 && !deviceRepl) throw new InvalidOperationException("设备正在执行任务，请先停止并连接 REPL。");
        FlushOutput();
        string text = TerminalDraft(channel);
        state.Sending = true;
        try
        {
            var ed = consoles[channel].Editor;
            state.Changing = true;
            string executable = channel == 1 ? Path.GetFileNameWithoutExtension(PyTouch.Core.ShellCommand.Resolve(settings).Executable) : "";
            bool remoteEcho = channel == 3 || executable.Equals("pwsh", StringComparison.OrdinalIgnoreCase) || executable.Equals("powershell", StringComparison.OrdinalIgnoreCase);
            ed.ReadOnly = false;
            if (remoteEcho) { ed.SetSel(state.InputStart, ed.Length); ed.ReplaceSel(""); }
            else { ed.GotoPos(ed.Length); ed.ReplaceSel("\n"); }
            state.InputStart = ed.Length;
            state.Changing = false; ProtectTerminal(channel);
            if (text.Length > 0 && (state.History.Count == 0 || state.History[^1] != text)) state.History.Add(text);
            if (state.History.Count > 200) state.History.RemoveAt(0);
            state.HistoryIndex = state.History.Count; state.Draft = "";
            await session.SendAsync(channel == 3 ? System.Text.Json.JsonSerializer.Serialize(new { text = text.Replace("\n", "\r") + "\r" }) : text);
        }
        finally { state.Changing = false; state.Sending = false; ProtectTerminal(channel); }
    }
    async Task TerminalKeyAsync(int channel, KeyRoutedEventArgs e)
    {
        var ed = consoles[channel].Editor; var state = terminalStates[channel];
        bool ctrl = KeyHeld(VirtualKey.Control), shift = KeyHeld(VirtualKey.Shift);
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await SubmitTerminalAsync(channel); return; }
        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;
            if (!shift)
            {
                if (ed.SelectionStart < state.InputStart) ed.GotoPos(ed.Length);
                ed.ReadOnly = false; ed.ReplaceSel("\t"); ProtectTerminal(channel);
            }
            return;
        }
        if (ctrl && e.Key == VirtualKey.C)
        {
            e.Handled = true;
            if (ed.SelectionStart != ed.SelectionEnd) ed.Copy();
            else if (channel == 3 && deviceRepl) await InterruptDeviceAsync();
            else { new[] { runner, shell, repl, device }[channel].Stop(); ReplaceTerminalDraft(channel, ""); AppendTerminalOutput(channel, "^C\n"); }
            return;
        }
        if (ctrl && e.Key == VirtualKey.L) { e.Handled = true; ClearTerminal(channel); return; }
        if (ctrl && e.Key == VirtualKey.A) { e.Handled = true; ed.SetSel(state.InputStart, ed.Length); return; }
        if ((ctrl && e.Key == VirtualKey.V) || (shift && e.Key == VirtualKey.Insert))
        {
            e.Handled = true; var data = Clipboard.GetContent();
            if (!data.Contains(StandardDataFormats.Text)) return;
            string text = (await data.GetTextAsync()).Replace("\r\n", "\n").Replace('\r', '\n');
            if (ed.SelectionStart < state.InputStart) ed.GotoPos(ed.Length);
            ed.ReadOnly = false; ed.ReplaceSel(text); ProtectTerminal(channel); return;
        }
        // Other Scintilla letter shortcuts can operate on whole transcript lines.
        // Ctrl+X remains available when its selection is wholly in the input tail.
        if (ctrl && e.Key != VirtualKey.X && (int)e.Key is >= 65 and <= 90) { e.Handled = true; return; }
        if (e.Key is VirtualKey.Up or VirtualKey.Down && !ctrl && !shift)
        {
            e.Handled = true;
            MoveTerminalHistory(channel, e.Key == VirtualKey.Up ? -1 : 1); return;
        }
        if (e.Key == VirtualKey.Home && !ctrl)
        {
            e.Handled = true; ed.SetSel(shift ? ed.Anchor : state.InputStart, state.InputStart); return;
        }
        if (e.Key == VirtualKey.Back || (e.Key == VirtualKey.Left && !shift && !ctrl))
        {
            if (ed.SelectionStart < state.InputStart || (ed.SelectionStart == ed.SelectionEnd && ed.CurrentPos <= state.InputStart)) { e.Handled = true; return; }
            if (ctrl && e.Key == VirtualKey.Back)
            {
                e.Handled = true; long end = ed.CurrentPos; ed.WordLeft(); long start = Math.Max(state.InputStart, ed.CurrentPos);
                ed.ReadOnly = false; ed.SetSel(start, end); ed.ReplaceSel(""); return;
            }
        }
        if (e.Key == VirtualKey.Delete || (ctrl && e.Key == VirtualKey.X))
        {
            if (ed.SelectionStart < state.InputStart) { e.Handled = true; if (ctrl) ed.Copy(); return; }
        }
        // Typing after selecting output starts at the live prompt. Read-only
        // selection still permits copying the transcript, including touch copy.
        bool editingKey = e.Key == VirtualKey.Space || e.Key == VirtualKey.Tab || (int)e.Key is >= 48 and <= 111 or >= 186 and <= 254;
        if (!ctrl && editingKey && ed.SelectionStart < state.InputStart) ed.GotoPos(ed.Length);
        ProtectTerminal(channel);
    }
}

