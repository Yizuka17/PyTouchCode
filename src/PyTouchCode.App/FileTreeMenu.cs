using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PyTouchCode.Core;
using Windows.ApplicationModel.DataTransfer;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    void FileTreeContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        TreeViewNode? node = null;
        for (var element = args.OriginalSource as DependencyObject; element is not null && element != FileTree; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { Tag: TreeViewNode tagged }) { node = tagged; break; }
            if (element is TreeViewItem row) { node = FileTree.NodeFromContainer(row); break; }
        }
        bool hasPosition = args.TryGetPosition(FileTree, out var position);
        if (!hasPosition) node ??= FileTree.SelectedNode;
        var entry = node?.Content as FileTreeEntry;
        if (entry is null && !Directory.Exists(settings.Workspace)) return;
        entry ??= new FileTreeEntry(settings.Workspace, true);
        if (node is not null) FileTree.SelectedNode = node;
        var menu = CreateFileMenu(entry);
        if (hasPosition) menu.ShowAt(FileTree, new FlyoutShowOptions { Position = position });
        else menu.ShowAt(FileTree);
        args.Handled = true;
    }

    MenuFlyout CreateFileMenu(FileTreeEntry entry)
    {
        var menu = new MenuFlyout();
        void Item(string text, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text };
            SetUiFont(item, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
            item.Click += async (_, _) => await SafeAsync(action); menu.Items.Add(item);
        }
        string parent = entry.Directory ? entry.Path : Path.GetDirectoryName(entry.Path)!;
        Item("新建文件", () => NameEntryAsync(parent, false));
        Item("新建文件夹", () => NameEntryAsync(parent, true));
        menu.Items.Add(new MenuFlyoutSeparator());
        if (!entry.Directory) Item("打开", () => OpenDocumentAsync(entry.Path));
        Item("重命名", () => NameEntryAsync(entry.Path, entry.Directory, true));
        Item("在资源管理器中显示", () =>
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add("/select," + entry.Path); System.Diagnostics.Process.Start(start); return Task.CompletedTask;
        });
        Item("复制路径", () => { var data = new DataPackage(); data.SetText(entry.Path); Clipboard.SetContent(data); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("刷新", RefreshFilesAsync);
        return menu;
    }

    async Task NameEntryAsync(string path, bool folder, bool rename = false)
    {
        var name = new TextBox { Text = rename ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : folder ? "新建文件夹" : "新文件.py", MinWidth = 260 };
        name.Loaded += (_, _) => { name.Focus(FocusState.Programmatic); name.Select(0, rename && !folder ? Path.GetFileNameWithoutExtension(name.Text).Length : name.Text.Length); };
        var dialog = new ContentDialog { Title = rename ? "重命名" : folder ? "新建文件夹" : "新建文件", Content = name, PrimaryButtonText = rename ? "重命名" : "创建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                string parent = rename ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? throw new IOException("无法重命名磁盘根目录。") : path;
                string target = WorkspaceFiles.ChildPath(parent, name.Text);
                if (Path.Exists(target) && (!rename || !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))) throw new IOException("同名文件或文件夹已存在。");
            }
            catch (Exception e) { args.Cancel = true; name.Header = e.Message; }
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        if (rename) await RenameEntryAsync(path, name.Text);
        else
        {
            string created = WorkspaceFiles.Create(path, name.Text, folder);
            await RefreshFilesAsync(); await RevealFileAsync(created);
            if (!folder) await OpenDocumentAsync(created);
        }
    }

    async Task RenameEntryAsync(string path, string name)
    {
        string target = WorkspaceFiles.Rename(path, name);
        foreach (var tab in documents)
        {
            if (tab.Document.Path is not { } file) continue;
            tab.Document.Path = WorkspaceFiles.Rebase(file, path, target); UpdateTab(tab);
        }
        settings.Workspace = WorkspaceFiles.Rebase(settings.Workspace, path, target);
        settings.RecentFiles = settings.RecentFiles.Select(p => WorkspaceFiles.Rebase(p, path, target)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.RecentFolders = settings.RecentFolders.Select(p => WorkspaceFiles.Rebase(p, path, target)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await store.SaveAsync(settings); RefreshRecent(); await RefreshFilesAsync(); await RevealFileAsync(target);
    }

    async Task RevealFileAsync(string path)
    {
        var node = FileTree.RootNodes.FirstOrDefault();
        while (node?.Content is FileTreeEntry entry)
        {
            if (string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)) { FileTree.SelectedNode = node; return; }
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(entry.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            if (node.HasUnrealizedChildren) await LoadChildrenAsync(node);
            node.IsExpanded = true;
            node = node.Children.FirstOrDefault(child => child.Content is FileTreeEntry file &&
                (string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(file.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
