using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PyTouchCode.Core;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    readonly WorkspaceSessionStore sessionStore;
    readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    bool sessionReady, sessionAvailable = true, closePending;

    void ScheduleSessionSave()
    {
        if (!sessionReady || !sessionAvailable || closing || closePending) return;
        sessionTimer.Stop(); sessionTimer.Start();
    }
    WorkspaceSession CaptureSession()
    {
        var ordered = Tabs.TabItems.OfType<TabViewItem>().Select(item => (EditorTab)item.Tag).ToList();
        var session = new WorkspaceSession { ActiveIndex = Active is { } active ? ordered.IndexOf(active) : -1 };
        foreach (var tab in ordered)
        {
            var doc = tab.Document; var ed = tab.Control.Editor;
            session.Documents.Add(new WorkspaceDocument
            {
                Path = doc.Path, Text = doc.Dirty || doc.Path is null ? tab.Initialized ? ed.GetText(ed.Length + 1) : doc.Text : null,
                EncodingName = doc.EncodingName, Bom = doc.Bom, NewLine = doc.NewLine, DiskHash = doc.DiskHash,
                Anchor = tab.PendingSessionView?.Anchor ?? (tab.Initialized ? ed.Anchor : 0),
                Caret = tab.PendingSessionView?.Caret ?? (tab.Initialized ? ed.CurrentPos : 0),
                FirstVisibleLine = tab.PendingSessionView?.FirstVisibleLine ?? (tab.Initialized ? ed.DocLineFromVisible(ed.FirstVisibleLine) : 0),
                XOffset = tab.PendingSessionView?.XOffset ?? (tab.Initialized ? ed.XOffset : 0)
            });
        }
        return session;
    }
    Task SaveSessionAsync() => sessionReady && sessionAvailable ? sessionStore.SaveAsync(CaptureSession()) : Task.CompletedTask;

    async Task CloseWorkspaceAsync()
    {
        if (closePending || dialogOpen) return;
        closePending = true;
        try
        {
            if (!sessionAvailable) foreach (var tab in documents.ToArray()) if (!await ConfirmSaveAsync(tab)) return;
            Root.IsHitTestVisible = false; RunButton.Focus(FocusState.Programmatic);
            sessionTimer.Stop(); await SaveSessionAsync(); await store.SaveAsync(settings);
            runner.Stop(); shell.Stop(); repl.Stop(); maintenance.Stop(); await StopDeviceAsync();
            if (debugger is not null) await debugger.DisposeAsync();
            language.Stop(); closing = true; Close();
        }
        finally { closePending = false; if (!closing) Root.IsHitTestVisible = true; }
    }

    async Task RestoreSessionAsync()
    {
        WorkspaceSession? session;
        try { session = await sessionStore.LoadAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            sessionAvailable = false; Notify("工作区未恢复：" + e.Message); return;
        }
        if (session is null)
        {
            // Earlier versions recorded recent files but had no open-tab session.
            if (settings.RecentFiles.FirstOrDefault(File.Exists) is { } recent)
                try { AddDocument(await DocumentFiles.OpenAsync(recent)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Notify("文件未打开：" + e.Message); }
            return;
        }
        EditorTab? active = null; int skipped = 0;
        for (int index = 0; index < session.Documents.Count; index++)
        {
            var state = session.Documents[index];
            try
            {
                if (await state.RestoreAsync() is not { } document) continue;
                AddDocument(document, state);
                if (index == session.ActiveIndex) active = documents[^1];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { skipped++; }
        }
        if (active is not null) Tabs.SelectedItem = active.Item;
        if (skipped > 0) Notify($"上次打开的 {skipped} 个文件暂时无法读取。");
    }
    void RestoreSessionView(EditorTab tab)
    {
        if (tab.PendingSessionView is not { } state || !tab.Initialized || tab.Control.ActualWidth < 1 || tab.Control.ActualHeight < 1) return;
        var ed = tab.Control.Editor;
        ed.SetSel(Math.Clamp(state.Anchor, 0, ed.Length), Math.Clamp(state.Caret, 0, ed.Length));
        ed.ScrollCaret();
        S(tab.Control, 2613, (ulong)Math.Max(0, ed.VisibleFromDocLine(Math.Clamp(state.FirstVisibleLine, 0, ed.LineCount - 1))));
        ed.XOffset = Math.Max(0, state.XOffset);
        tab.PendingSessionView = null;
    }
}
