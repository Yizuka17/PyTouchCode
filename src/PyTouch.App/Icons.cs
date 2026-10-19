using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Xml.Linq;

namespace PyTouch.App;
public sealed partial class MainWindow
{
    static readonly Dictionary<string, string> IconPaths = new();
    static string IconPath(string key)
    {
        if (!IconPaths.TryGetValue(key, out string? data))
        {
            var svg = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", key + ".svg"));
            data = "F1 " + string.Join(" ", svg.Descendants().Where(e => e.Name.LocalName == "path").Select(e => (string)e.Attribute("d")!));
            IconPaths[key] = data;
        }
        return data;
    }
    static PathIcon CreateVectorIcon(string key) => (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{IconPath(key)}\"/>");
    // Preserve the original SVG viewport. Shrinking a PathIcon's layout box clips
    // its geometry; a Viewbox scales the whole 20 x 20 drawing, including padding.
    static Viewbox ToolbarIcon(string key, Control owner)
    {
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{IconPath(key)}\"/>");
        path.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.FillProperty, new Binding { Source = owner, Path = new PropertyPath("Foreground") });
        var canvas = new Canvas { Width = 20, Height = 20 }; canvas.Children.Add(path);
        return new Viewbox { Width = 20, Height = 20, Child = canvas, IsHitTestVisible = false };
    }
    static SvgImageSource FileImage(string key) => new(new Uri($"ms-appx:///Assets/FileIcons/{key}.svg"));
    static IconSource FileTypeIcon(string key) => new ImageIconSource { ImageSource = FileImage(key) };
    void AddMenuIcons()
    {
        ((StackPanel)RunButton.Content).Children.Insert(0, ToolbarIcon("Run", RunButton));
        StopButton.Content = ToolbarIcon("Stop", StopButton);
        SettingsButton.Content = ToolbarIcon("Settings", SettingsButton);
        RefreshFilesButton.Content = ToolbarIcon("Refresh", RefreshFilesButton);
        CollapseOutputButton.Content = ToolbarIcon("Close", CollapseOutputButton);
        void Visit(IEnumerable<MenuFlyoutItemBase> items)
        {
            foreach (var item in items)
            {
                if (item is MenuFlyoutSubItem sub) { Visit(sub.Items); continue; }
                if (item is not MenuFlyoutItem { Tag: string command } action) continue;
                string? key = command switch
                {
                    "new" or "open" => "Document",
                    "folder" => "Folder",
                    "save" or "saveas" or "saveall" => "Save",
                    "run" or "debug" or "continue" => "Run",
                    "stop" or "closeterminal" => "Stop",
                    "close" => "Close",
                    "reload" or "rerun" or "updates" => "Refresh",
                    "settings" or "environment" => "Settings",
                    "find" => "Search",
                    _ => null
                };
                if (key is not null) action.Icon = CreateVectorIcon(key);
            }
        }
        foreach (var menu in MainMenu.Items) Visit(menu.Items);
    }
}

