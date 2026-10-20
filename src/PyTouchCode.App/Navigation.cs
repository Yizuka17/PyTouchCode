using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    Grid NavigationTabs(params (string Title, UIElement Page)[] pages)
    {
        var navigation = new NavigationView
        {
            CompactPaneLength = 0, OpenPaneLength = 0, IsPaneOpen = false, IsPaneToggleButtonVisible = false, IsTitleBarAutoPaddingEnabled = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
            IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            AlwaysShowHeader = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        foreach (var page in pages)
        {
            var item = new NavigationViewItem { Content = page.Title, Tag = page.Page };
            SetUiFont(item, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
            navigation.MenuItems.Add(item);
        }
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        navigation.Height = 48; navigation.Tag = content;
        navigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem { Tag: UIElement page }) content.Content = page;
        };
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(navigation); Grid.SetRow(content, 1); panel.Children.Add(content);
        if (navigation.MenuItems.Count > 0) navigation.SelectedItem = navigation.MenuItems[0];
        return panel;
    }
    void SidebarNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (FilesSidebarPanel is null || DebugSidebarPanel is null) return;
        bool debug = args.SelectedItem is NavigationViewItem { Tag: "debug" };
        FilesSidebarPanel.Visibility = debug ? Visibility.Collapsed : Visibility.Visible;
        DebugSidebarPanel.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
    }
}
