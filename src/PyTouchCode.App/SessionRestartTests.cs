using Microsoft.UI.Xaml.Controls;
using PyTouchCode.Core;
using System.Text.Json;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    // Each phase is a separate process using the same isolated smoke directory.
    async Task RunSessionRestartProbeAsync(string directory, string phase)
    {
        Directory.CreateDirectory(directory);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Wait(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition()) await Task.Delay(25, timeout.Token);
        }
        async Task Select(EditorTab tab)
        {
            Tabs.SelectedItem = tab.Item;
            await Wait(() => tab.Initialized && tab.Control.ActualWidth > 0 && tab.Control.ActualHeight > 0 && tab.PendingSessionView is null);
        }
        string firstPath = Path.Combine(directory, "first.py"), secondPath = Path.Combine(directory, "second.py");
        const string draft = "# 未保存的草稿🙂\nprint('restored')\n";
        const string changed = "print('on disk')\n# 修改尚未保存🙂\n";
        try
        {
            if (phase.StartsWith("empty"))
                Assert(documents.Count == 0 && Active is null, "Empty workspace acquired an unsolicited untitled tab");
            else if (phase == "seed")
            {
                Assert(documents.Count == 0, "Startup created a welcome script");
                settings.Completion = false;
                await File.WriteAllTextAsync(firstPath, string.Concat(Enumerable.Range(0, 160).Select(n => $"# 中文 {n} {new string('x', 180)}\n")));
                await File.WriteAllTextAsync(secondPath, "print('on disk')\n");
                string missing = Path.Combine(directory, "missing.py"); await File.WriteAllTextAsync(missing, "# removable drive\n");
                await OpenDocumentAsync(firstPath); var first = Active!; await Select(first);
                await OpenDocumentAsync(missing);
                await OpenDocumentAsync(secondPath); await Select(Active!); Active!.Control.Editor.SetText(changed);
                NewDocument(); await Select(Active!); Active!.Control.Editor.SetText(draft);
                Tabs.TabItems.Remove(first.Item); Tabs.TabItems.Insert(1, first.Item);
                await Select(first);
                var ed = first.Control.Editor;
                ed.SetSel(ed.PositionFromLine(40) + 2, ed.PositionFromLine(40) + 12);
                ed.ScrollCaret(); S(first.Control, 2613, 30); ed.XOffset = 60;
                await File.WriteAllTextAsync(Path.Combine(directory, "expected-view.json"), JsonSerializer.Serialize(new { anchor = ed.Anchor, caret = ed.CurrentPos, line = ed.DocLineFromVisible(ed.FirstVisibleLine), x = ed.XOffset }));
                await Wait(() => File.Exists(sessionStore.FilePath));
                await Task.Delay(1100);
                var autosaved = await sessionStore.LoadAsync();
                Assert(autosaved is { Documents.Count: 4, ActiveIndex: 1 } && autosaved.Documents[0].Path == missing && autosaved.Documents[2].Text == changed && autosaved.Documents[3].Text == draft, "Debounced session save lost drafts or reordered tabs");
            }
            else if (phase == "restore")
            {
                Assert(documents.Count == 3 && documents[0].Document.Path == firstPath && documents[1].Document.Path == secondPath && documents[2].Document.Path is null, "Restart lost tab order or failed to skip a missing file");
                var first = documents[0]; Assert(Active == first, "Restart selected the wrong tab"); await Select(first);
                using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "expected-view.json")));
                var ed = first.Control.Editor; var view = expected.RootElement;
                Assert(ed.Anchor == view.GetProperty("anchor").GetInt64() && ed.CurrentPos == view.GetProperty("caret").GetInt64(), "Restart lost Unicode selection offsets");
                Assert(ed.DocLineFromVisible(ed.FirstVisibleLine) == view.GetProperty("line").GetInt64() && ed.XOffset == view.GetProperty("x").GetInt32(), "Restart lost scroll position");
                Assert(ed.GetText(ed.Length + 1).EndsWith("# external change\n"), "Clean file was restored from stale text");
                await Select(documents[1]); var second = Active!;
                Assert(second.Document.Dirty && second.Control.Editor.GetText(second.Control.Editor.Length + 1) == changed, "Restart lost unsaved file changes");
                second.Control.Editor.AddText(1, "x"); second.Control.Editor.Undo();
                Assert(second.Document.Dirty, "Undo incorrectly marked recovered unsaved text as saved");
                await SaveDocumentToPathAsync(second, secondPath);
                Assert(!second.Document.Dirty && await File.ReadAllTextAsync(secondPath) == changed, "Recovered edits could not be saved");
                await Select(documents[2]); var untitled = Active!;
                Assert(untitled.Document.Dirty && untitled.Control.Editor.GetText(untitled.Control.Editor.Length + 1) == draft, "Restart lost the untitled draft");
                untitled.Document.Dirty = false; // Simulate the explicit discard choice without a modal.
                await CloseTabAsync(untitled);
                foreach (var tab in documents.ToArray()) await CloseTabAsync(tab);
                Assert(documents.Count == 0 && Active is null, "Closing the last tab created an untitled document");
            }
            else throw new InvalidOperationException("Unknown session test phase");
            await File.WriteAllTextAsync(Path.Combine(directory, "session-" + phase + ".json"), JsonSerializer.Serialize(new { phase, passed = true }));
            await CloseWorkspaceAsync();
        }
        catch (Exception e)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "session-" + phase + ".json"), JsonSerializer.Serialize(new { phase, passed = false, error = e.ToString() }));
            sessionTimer.Stop(); closing = true; Close();
        }
    }
}
