using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PyTouch.Core;
using Windows.Storage.Pickers;
using WinUIEditor;

namespace PyTouch.App;
public sealed partial class MainWindow
{
    void NewDocument(string text = "") => AddDocument(new Document { Text = text });
    void AddDocument(Document document)
    {
        var control = new EditorBaseControl();
        var ghost = new TextBlock { IsHitTestVisible = false, Opacity = .45, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        var grid = new Grid(); grid.Children.Add(control); grid.Children.Add(ghost);
        var handles = new Canvas { IsTabStop = false };
        var startHandle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), Visibility = Visibility.Collapsed };
        var endHandle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), Visibility = Visibility.Collapsed };
        handles.Children.Add(startHandle); handles.Children.Add(endHandle); grid.Children.Add(handles);
        var item = new TabViewItem { Header = document.Name };
        var tab = new EditorTab { Document = document, Control = control, Item = item, View = grid, Ghost = ghost, Handles = handles, StartHandle = startHandle, EndHandle = endHandle };
        item.Tag = tab; documents.Add(tab); EditorHost.Children.Add(grid); Tabs.TabItems.Add(item); Tabs.SelectedItem = item;
        control.Loaded += (_, _) =>
        {
            if (tab.Initialized) return; tab.Loading = true;
            ConfigureEditor(control, false); control.Editor.SetText(document.Text); control.Editor.EmptyUndoBuffer(); control.Editor.SetSavePoint();
            tab.Loading = false; tab.Initialized = true;
        };
        control.Editor.Modified += (_, args) =>
        {
            if (tab.Loading || (args.ModificationType & 3) == 0) return;
            document.Dirty = true; tab.Revision++; tab.Ghost.Visibility = Visibility.Collapsed; tab.GhostInsertion = "";
            UpdateTab(tab); tab.CompletionCancellation?.Cancel(); tab.CompletionTimer.Stop();
            if (settings.Completion && File.Exists(settings.Interpreter)) tab.CompletionTimer.Start();
        };
        control.Editor.SavePointReached += (_, _) => { if (!tab.Loading) { document.Dirty = false; UpdateTab(tab); } };
        control.Editor.UpdateUI += (_, args) => { UpdatePosition(tab); UpdateSelectionHandles(tab); if ((args.Updated & 2) != 0) { tab.Ghost.Visibility = Visibility.Collapsed; tab.GhostInsertion = ""; } };
        control.Editor.CharAdded += (_, args) => AutoPairAndIndent(tab, args.Ch);
        control.Editor.MarginClick += async (_, args) => await SafeAsync(async () =>
        {
            if (args.Margin == 1) await ToggleBreakpointAsync(tab, (int)control.Editor.LineFromPosition(args.Position));
            else if (args.Margin == 2) control.Editor.ToggleFold(control.Editor.LineFromPosition(args.Position));
        });
        control.Editor.DwellStart += async (_, args) => await SafeAsync(() => HoverAsync(tab, args.Position));
        control.Editor.DwellEnd += (_, _) => control.Editor.CallTipCancel();
        control.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Tab && !string.IsNullOrEmpty(tab.GhostInsertion) && !control.Editor.AutoCActive())
            { string value = tab.GhostInsertion; tab.GhostInsertion = ""; ghost.Visibility = Visibility.Collapsed; control.Editor.ReplaceSel(value); args.Handled = true; }
        };
        tab.CompletionTimer.Tick += async (_, _) => { tab.CompletionTimer.Stop(); await SafeAsync(() => CompleteAsync(tab, false)); };
        AttachTouch(control, () => { tab.ShowHandles = true; UpdateSelectionHandles(tab); });
        AttachSelectionHandle(tab, startHandle, true); AttachSelectionHandle(tab, endHandle, false);
        UpdateTab(tab);
    }
    void UpdateTab(EditorTab tab)
    {
        string key = FileIcons.Resolve(tab.Document.Path ?? tab.Document.Name);
        if (key != tab.IconKey) { tab.Item.IconSource = FileTypeIcon(key); tab.IconKey = key; }
        tab.Item.Header = tab.Document.Name + (tab.Document.Dirty ? " ●" : "");
        ToolTipService.SetToolTip(tab.Item, tab.Document.Path ?? "尚未保存");
        if (Active == tab) { Title = tab.Document.Name + " — PyTouchCode"; UpdatePosition(tab); }
    }
    void UpdatePosition(EditorTab tab)
    {
        if (Active != tab) return;
        var ed = tab.Control.Editor;
        PositionStatus.Text = $"行 {ed.LineFromPosition(ed.CurrentPos) + 1}，列 {ed.GetColumn(ed.CurrentPos) + 1}";
        EncodingStatus.Text = $"{tab.Document.EncodingName.ToUpperInvariant()}{(tab.Document.Bom ? " BOM" : "")} {(tab.Document.NewLine == "\n" ? "LF" : "CRLF")} {settings.IndentSize} {(settings.UseTabs ? "Tab" : "空格")}";
    }
    async Task OpenDocumentAsync(string path)
    {
        var existing = documents.FirstOrDefault(x => string.Equals(x.Document.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { Tabs.SelectedItem = existing.Item; return; }
        Document doc;
        try { doc = await DocumentFiles.OpenAsync(path); }
        catch (System.Text.DecoderFallbackException)
        {
            var result = await ShowDialogAsync(new ContentDialog { Title = "文件不是有效的 UTF-8", Content = "使用 GB18030 编码重新打开此文件？", PrimaryButtonText = "使用 GB18030", CloseButtonText = "取消" });
            if (result != ContentDialogResult.Primary) return;
            doc = await DocumentFiles.OpenAsync(path, "gb18030");
        }
        AddDocument(doc); SettingsStore.Remember(settings.RecentFiles, doc.Path!); await store.SaveAsync(settings); RefreshRecent();
    }
    async Task PickOpenAsync()
    {
        var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*"); var file = await picker.PickSingleFileAsync(); if (file is not null) await OpenDocumentAsync(file.Path);
    }
    async Task PickFolderAsync()
    {
        var picker = new FolderPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*"); var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        settings.Workspace = folder.Path; SettingsStore.Remember(settings.RecentFolders, folder.Path); await store.SaveAsync(settings); RefreshRecent(); await RefreshFilesAsync();
    }
    async Task<bool> SaveDocumentAsync(EditorTab tab, bool saveAs = false)
    {
        string? path = tab.Document.Path;
        if (saveAs || path is null)
        {
            var picker = new FileSavePicker { SuggestedFileName = tab.Document.Name };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Python", new List<string> { ".py" }); picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
            var file = await picker.PickSaveFileAsync(); if (file is null) return false; path = file.Path;
        }
        return await SaveDocumentToPathAsync(tab, path);
    }
    async Task<bool> SaveDocumentToPathAsync(EditorTab tab, string path)
    {
        tab.Document.Text = tab.Control.Editor.GetText(tab.Control.Editor.Length + 1);
        await DocumentFiles.SaveAsync(tab.Document, path);
        tab.Control.Editor.SetSavePoint(); UpdateTab(tab);
        SettingsStore.Remember(settings.RecentFiles, path); await store.SaveAsync(settings); RefreshRecent(); return true;
    }
    async Task<bool> ConfirmSaveAsync(EditorTab tab)
    {
        if (!tab.Document.Dirty) return true;
        Tabs.SelectedItem = tab.Item;
        var result = await ShowDialogAsync(new ContentDialog { Title = "保存修改？", Content = tab.Document.Name, PrimaryButtonText = "保存", SecondaryButtonText = "不保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary });
        return result == ContentDialogResult.Secondary || result == ContentDialogResult.Primary && await SaveDocumentAsync(tab);
    }
    async Task CloseTabAsync(EditorTab tab)
    {
        if (!await ConfirmSaveAsync(tab)) return;
        tab.CompletionCancellation?.Cancel(); tab.CompletionTimer.Stop(); documents.Remove(tab); Tabs.TabItems.Remove(tab.Item); EditorHost.Children.Remove(tab.View);
        if (documents.Count == 0) NewDocument();
    }
    async Task ReloadAsync(EditorTab tab)
    {
        if (tab.Document.Path is null) return;
        if (tab.Document.Dirty)
        {
            var result = await ShowDialogAsync(new ContentDialog { Title = "重新加载文件？", Content = "这将放弃当前标签页未保存的修改。", PrimaryButtonText = "重新加载", CloseButtonText = "取消" });
            if (result != ContentDialogResult.Primary) return;
        }
        var doc = await DocumentFiles.OpenAsync(tab.Document.Path);
        tab.Loading = true; tab.Control.Editor.SetText(doc.Text); tab.Control.Editor.EmptyUndoBuffer(); tab.Control.Editor.SetSavePoint();
        tab.Document.Text = doc.Text; tab.Document.EncodingName = doc.EncodingName; tab.Document.Bom = doc.Bom; tab.Document.NewLine = doc.NewLine; tab.Document.DiskHash = doc.DiskHash; tab.Document.Dirty = false;
        tab.Loading = false; tab.BreakpointHandles.Clear(); UpdateTab(tab);
    }
    void NewTabClicked(TabView sender, object args) => NewDocument();
    async void TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) { if (args.Tab.Tag is EditorTab tab) await SafeAsync(() => CloseTabAsync(tab)); }
    void TabSelectionChanged(object sender, SelectionChangedEventArgs e) { foreach (var document in documents) document.View.Visibility = document == Active ? Visibility.Visible : Visibility.Collapsed; if (Active is { } tab) { UpdateTab(tab); tab.Control.Focus(FocusState.Programmatic); } }
    async Task RefreshFilesAsync()
    {
        FileTree.RootNodes.Clear(); ViewModel.WorkspaceLabel = Directory.Exists(settings.Workspace) ? settings.Workspace : "Python 工作区";
        if (!Directory.Exists(settings.Workspace)) return;
        var node = CreateFileNode(new FileNode(settings.Workspace, true)); FileTree.RootNodes.Add(node);
        await LoadChildrenAsync(node); node.IsExpanded = true;
    }
    async Task LoadChildrenAsync(TreeViewNode node)
    {
        if (node.Content is not FrameworkElement { Tag: FileNode value } || !value.Directory) return;
        var entries = await Task.Run(() =>
        {
            var directory = new DirectoryInfo(value.Path);
            return directory.EnumerateFileSystemInfos().Where(x => (x.Attributes & FileAttributes.ReparsePoint) == 0 && x.Name is not (".git" or "__pycache__" or ".venv" or "node_modules"))
                .OrderByDescending(x => (x.Attributes & FileAttributes.Directory) != 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(2000)
                .Select(x => new FileNode(x.FullName, (x.Attributes & FileAttributes.Directory) != 0)).ToList();
        });
        node.Children.Clear(); foreach (var entry in entries) node.Children.Add(CreateFileNode(entry));
        node.HasUnrealizedChildren = false;
    }
    async void FileExpanding(TreeView sender, TreeViewExpandingEventArgs args) { UpdateFolderImage(args.Node, true); if (args.Node.HasUnrealizedChildren) await SafeAsync(() => LoadChildrenAsync(args.Node)); }
    void FileCollapsed(TreeView sender, TreeViewCollapsedEventArgs args) => UpdateFolderImage(args.Node, false);
    async void FileInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) { if (args.InvokedItem is TreeViewNode { Content: FrameworkElement { Tag: FileNode { Directory: false } file } }) await SafeAsync(() => OpenDocumentAsync(file.Path)); }
    void RefreshRecent()
    {
        RecentMenu.Items.Clear();
        foreach (var path in settings.RecentFiles.Where(File.Exists)) { var item = new MenuFlyoutItem { Text = path }; item.Click += async (_, _) => await SafeAsync(() => OpenDocumentAsync(path)); RecentMenu.Items.Add(item); }
        if (settings.RecentFolders.Count > 0) RecentMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var path in settings.RecentFolders.Where(Directory.Exists)) { var item = new MenuFlyoutItem { Text = "文件夹 " + path }; item.Click += async (_, _) => await SafeAsync(async () => { settings.Workspace = path; await store.SaveAsync(settings); await RefreshFilesAsync(); }); RecentMenu.Items.Add(item); }
        ApplyMenuFonts();
        if (RecentMenu.Items.Count == 0) RecentMenu.Items.Add(new MenuFlyoutItem { Text = "尚无最近文件", IsEnabled = false });
    }
    sealed record FileNode(string Path, bool Directory);
    static TreeViewNode CreateFileNode(FileNode file)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = file };
        row.Children.Add(new Image { Width = 20, Height = 20, Source = FileImage(FileIcons.Resolve(file.Path, file.Directory)) });
        string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(file.Path));
        row.Children.Add(new TextBlock { Text = name.Length > 0 ? name : file.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        ToolTipService.SetToolTip(row, file.Path);
        return new TreeViewNode { Content = row, HasUnrealizedChildren = file.Directory };
    }
    static void UpdateFolderImage(TreeViewNode node, bool expanded)
    {
        if (node.Content is StackPanel { Tag: FileNode { Directory: true } file } row) ((Image)row.Children[0]).Source = FileImage(FileIcons.Resolve(file.Path, true, expanded));
    }
    async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (dialogOpen) return ContentDialogResult.None;
        dialogOpen = true; dialog.XamlRoot = Root.XamlRoot; dialog.RequestedTheme = Root.ActualTheme;
        dialog.FontFamily = new FontFamily(settings.UiFont); dialog.FontSize = settings.UiFontSize; dialog.FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)settings.UiFontWeight };
        try { return await dialog.ShowAsync(); } finally { dialogOpen = false; }
    }
}



