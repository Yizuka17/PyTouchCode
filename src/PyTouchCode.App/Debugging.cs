using Microsoft.UI.Xaml.Controls;
using PyTouchCode.Core;
using System.Text.Json;
using WinUIEditor;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    IEnumerable<int> BreakpointLines(EditorTab tab)
    {
        foreach (int handle in tab.BreakpointHandles) { long line = tab.Control.Editor.MarkerLineFromHandle(handle); if (line >= 0) yield return (int)line + 1; }
    }
    async Task ToggleBreakpointAsync(EditorTab tab, int line)
    {
        var ed = tab.Control.Editor;
        int? handle = tab.BreakpointHandles.Cast<int?>().FirstOrDefault(h => ed.MarkerLineFromHandle(h!.Value) == line);
        if (handle.HasValue) { ed.MarkerDeleteHandle(handle.Value); tab.BreakpointHandles.Remove(handle.Value); }
        else tab.BreakpointHandles.Add((int)ed.MarkerAdd(line, 0));
        if (debugger?.IsRunning == true && tab.Document.Path is not null) await debugger.SetBreakpointsAsync(tab.Document.Path, BreakpointLines(tab));
    }
    void DebugEvent(string name, JsonElement body)
    {
        DispatcherQueue.TryEnqueue(async () => await SafeAsync(async () =>
        {
            if (name is "output" or "adapterError") { if (body.TryGetProperty("output", out var output)) QueueOutput(0, output.GetString() ?? ""); return; }
            if (name == "continued") { foreach (var tab in documents) tab.Control.Editor.MarkerDeleteAll(1); DebugState.Text = "调试运行中"; return; }
            if (name == "terminated") { DebugState.Text = "调试结束"; debugThread = 0; foreach (var tab in documents) tab.Control.Editor.MarkerDeleteAll(1); var ended = debugger; debugger = null; if (ended is not null) await ended.DisposeAsync(); return; }
            if (name != "stopped" || debugger is null) return;
            debugThread = body.GetProperty("threadId").GetInt32();
            DebugState.Text = "已暂停 " + (body.TryGetProperty("description", out var description) ? description.GetString() : body.GetProperty("reason").GetString());
            var trace = await debugger.RequestAsync("stackTrace", new { threadId = debugThread, startFrame = 0, levels = 20 });
            VariablesTree.RootNodes.Clear();
            foreach (var frame in trace.GetProperty("stackFrames").EnumerateArray())
            {
                int id = frame.GetProperty("id").GetInt32(); string frameName = frame.GetProperty("name").GetString()!;
                int line = frame.GetProperty("line").GetInt32();
                var node = new TreeViewNode { Content = new DebugNode($"{frameName} 行 {line}", 0, id), HasUnrealizedChildren = true };
                VariablesTree.RootNodes.Add(node);
                if (VariablesTree.RootNodes.Count == 1)
                {
                    if (frame.TryGetProperty("source", out var source) && source.TryGetProperty("path", out var path) && File.Exists(path.GetString()))
                    {
                        await OpenDocumentAsync(path.GetString()!); var ed = Active!.Control.Editor;
                        foreach (var tab in documents) tab.Control.Editor.MarkerDeleteAll(1);
                        ed.MarkerAdd(Math.Max(0, line - 1), 1); ed.GotoLine(Math.Max(0, line - 1));
                    }
                    await LoadDebugNodeAsync(node); node.IsExpanded = true;
                }
            }
        }));
    }
    async Task LoadDebugNodeAsync(TreeViewNode node)
    {
        if (debugger is null || node.Content is not DebugNode data) return;
        node.Children.Clear();
        if (data.Frame != 0)
        {
            var response = await debugger.RequestAsync("scopes", new { frameId = data.Frame });
            foreach (var scope in response.GetProperty("scopes").EnumerateArray())
            {
                int reference = scope.GetProperty("variablesReference").GetInt32();
                var child = new TreeViewNode { Content = new DebugNode(scope.GetProperty("name").GetString()!, reference, 0), HasUnrealizedChildren = reference > 0 }; node.Children.Add(child);
                if (node.Children.Count == 1) { await LoadDebugNodeAsync(child); child.IsExpanded = true; }
            }
        }
        else if (data.Reference > 0)
        {
            var response = await debugger.RequestAsync("variables", new { variablesReference = data.Reference, start = 0, count = 200 });
            foreach (var variable in response.GetProperty("variables").EnumerateArray().Take(200))
            {
                int reference = variable.GetProperty("variablesReference").GetInt32();
                string type = variable.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                string value = variable.GetProperty("value").GetString() ?? ""; if (value.Length > 250) value = value[..250] + "…";
                node.Children.Add(new TreeViewNode { Content = new DebugNode($"{variable.GetProperty("name").GetString()} ({type}) = {value}", reference, 0), HasUnrealizedChildren = reference > 0 });
            }
        }
        node.HasUnrealizedChildren = false;
    }
    async void VariableExpanding(TreeView sender, TreeViewExpandingEventArgs args) { if (args.Node.HasUnrealizedChildren) await SafeAsync(() => LoadDebugNodeAsync(args.Node)); }
    sealed record DebugNode(string Label, int Reference, int Frame) { public override string ToString() => Label; }
}


