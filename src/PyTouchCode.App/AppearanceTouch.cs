using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.Foundation;
using WinUIEditor;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    void ApplyAppearance()
    {
        ElementTheme theme = settings.Theme switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default };
        if (Root.RequestedTheme != theme) Root.RequestedTheme = theme;
        SetUiFont(Root, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
        ApplyMenuFonts();
        foreach (var item in SidebarNavigation.MenuItems.OfType<NavigationViewItem>()) SetUiFont(item, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
        if (settings.UseSystemAccent) FileSelectionIndicator.ClearValue(Border.BackgroundProperty);
        else FileSelectionIndicator.Background = new SolidColorBrush(ParseColor(settings.Accent));
        try
        {
            SystemBackdrop = settings.Material switch
            {
                "Mica" when Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() => new MicaBackdrop(),
                "MicaAlt" when Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "Acrylic" when Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() => new DesktopAcrylicBackdrop(),
                _ => null
            };
        }
        catch (System.Runtime.InteropServices.COMException) { SystemBackdrop = null; }
        Root.Background = SystemBackdrop is null ? (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (!settings.UseSystemAccent) { var color = ParseColor(settings.Accent); RunButton.Background = new SolidColorBrush(color); }
        else RunButton.ClearValue(Control.BackgroundProperty);
        foreach (var tab in documents) { ConfigureEditor(tab.Control, false); UpdateTab(tab); }
        foreach (var console in consoles) if (console is not null) ConfigureEditor(console, true);
    }
    void ApplyMenuFonts()
    {
        void Visit(IEnumerable<MenuFlyoutItemBase> items)
        {
            foreach (var item in items)
            {
                SetUiFont(item, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
                if (item is MenuFlyoutSubItem sub) Visit(sub.Items);
            }
        }
        foreach (var menu in MainMenu.Items)
        {
            SetUiFont(menu, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
            Visit(menu.Items);
        }
    }
    static void SetUiFont(DependencyObject element, FontFamily font, double? size = null, int? weight = null)
    {
        if (element is EditorBaseControl or IconElement) return;
        // Set the authored control's text font, then let its content inherit it.
        // Never descend into a control template: its glyphs use separate fonts.
        var fontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(weight ?? 400) };
        if (element is Control control) { control.FontFamily = font; if (size is { } s) control.FontSize = s; if (weight.HasValue) control.FontWeight = fontWeight; return; }
        if (element is TextBlock text) { text.FontFamily = font; if (size is { } s) text.FontSize = s; if (weight.HasValue) text.FontWeight = fontWeight; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) SetUiFont(VisualTreeHelper.GetChild(element, i), font, size, weight);
    }
    void UpdateSelectionHandles(EditorTab tab)
    {
        var ed = tab.Control.Editor; double scale = tab.Control.XamlRoot?.RasterizationScale ?? 1;
        void Position(Border handle, long position)
        {
            double x = ed.PointXFromPosition(position) / scale, y = (ed.PointYFromPosition(position) + ed.TextHeight(ed.LineFromPosition(position))) / scale;
            bool visible = tab.ShowHandles && ed.SelectionStart != ed.SelectionEnd && x >= 0 && x < tab.Control.ActualWidth && y >= 0 && y < tab.Control.ActualHeight - 24;
            handle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; Canvas.SetLeft(handle, x - 12); Canvas.SetTop(handle, y);
        }
        Position(tab.StartHandle, ed.SelectionStart); Position(tab.EndHandle, ed.SelectionEnd);
    }
    void AttachSelectionHandle(EditorTab tab, Border handle, bool start)
    {
        long other = 0; bool dragging = false;
        handle.PointerPressed += (_, args) => { dragging = true; other = start ? tab.Control.Editor.SelectionEnd : tab.Control.Editor.SelectionStart; handle.CapturePointer(args.Pointer); args.Handled = true; };
        handle.PointerMoved += (_, args) =>
        {
            if (!dragging) return;
            var point = args.GetCurrentPoint(tab.Control).Position; double scale = tab.Control.XamlRoot.RasterizationScale;
            long position = TouchPosition(tab.Control, point);
            if (position >= 0) { tab.Control.Editor.SetSel(other, position); UpdateSelectionHandles(tab); }
            args.Handled = true;
        };
        handle.PointerReleased += (_, args) => { dragging = false; handle.ReleasePointerCapture(args.Pointer); args.Handled = true; };
        handle.PointerCanceled += (_, _) => dragging = false;
    }
    static long TouchPosition(EditorBaseControl control, Point point)
    {
        double scale = control.XamlRoot?.RasterizationScale ?? 1;
        long position = control.Editor.PositionFromPoint((int)(point.X * scale), (int)(point.Y * scale));
        return position < 0 ? control.Editor.CurrentPos : Math.Clamp(position, 0, control.Editor.Length);
    }
    static void PanTouch(EditorBaseControl control, long selectionAnchor, long caret, long firstLine, long xOffset, double dx, double dy)
    {
        var ed = control.Editor;
        if (ed.Anchor != selectionAnchor || ed.CurrentPos != caret) ed.SetSel(selectionAnchor, caret);
        double scale = control.XamlRoot?.RasterizationScale ?? 1;
        S(control, 2613, (ulong)Math.Max(0, firstLine - (long)(dy * scale / Math.Max(1, ed.TextHeight(0)))));
        ed.XOffset = (int)Math.Clamp(xOffset - (long)(dx * scale), 0, int.MaxValue);
    }
    void AttachTouch(EditorBaseControl control, Action? selectionChanged = null)
    {
        var pointers = new Dictionary<uint, Point>();
        Point origin = default, previous = default;
        long anchor = 0, firstLine = 0, originalAnchor = 0, originalCaret = 0, xOffset = 0;
        string direction = "";
        double distance = 0, initialFont = 0, velocity = 0;
        DateTime lastMove = DateTime.UtcNow;
        var inertia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double remainder = 0;
        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        hold.Tick += (_, _) =>
        {
            hold.Stop(); if (pointers.Count != 1 || direction.Length > 0) return;
            var menu = new MenuFlyout();
            foreach (var action in new[] { ("选择", "select"), ("复制", "copy"), ("剪切", "cut"), ("粘贴", "paste"), ("全选", "selectall") })
            {
                var item = new MenuFlyoutItem { Text = action.Item1 }; string command = action.Item2;
                SetUiFont(item, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
                item.Click += (_, _) => { var ed = control.Editor; switch (command) { case "select": ed.SetSel(ed.WordStartPosition(anchor, true), ed.WordEndPosition(anchor, true)); selectionChanged?.Invoke(); break; case "copy": ed.Copy(); break; case "cut": if (!ed.ReadOnly) ed.Cut(); break; case "paste": if (!ed.ReadOnly) ed.Paste(); break; case "selectall": ed.SelectAll(); selectionChanged?.Invoke(); break; } };
                menu.Items.Add(item);
            }
            menu.ShowAt(control, new FlyoutShowOptions { Position = origin }); direction = "hold";
        };
        inertia.Tick += (_, _) =>
        {
            if (Math.Abs(velocity) < .25 || pointers.Count > 0) { inertia.Stop(); return; }
            double height = Math.Max(1, control.Editor.TextHeight(0));
            remainder += velocity; long lines = (long)(remainder / height);
            if (lines != 0) { control.Editor.LineScroll(0, lines); remainder -= lines * height; }
            velocity *= .91;
        };
        control.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch) return;
            Point point = e.GetCurrentPoint(control).Position;
            double scale = control.XamlRoot?.RasterizationScale ?? 1;
            long margin = Enumerable.Range(0, 5).Sum(i => S(control, 2243, (ulong)i));
            if (point.X < margin / scale || point.X > control.ActualWidth - 20 || point.Y > control.ActualHeight - 20) return;
            inertia.Stop(); pointers[e.Pointer.PointerId] = point; hold.Stop();
            if (pointers.Count == 1)
            {
                origin = previous = point; direction = ""; velocity = remainder = 0; lastMove = DateTime.UtcNow;
                firstLine = control.Editor.FirstVisibleLine; xOffset = control.Editor.XOffset;
                originalAnchor = control.Editor.Anchor; originalCaret = control.Editor.CurrentPos;
                anchor = TouchPosition(control, point); hold.Start();
            }
            else if (pointers.Count == 2)
            {
                var values = pointers.Values.ToArray(); distance = Math.Max(1, Math.Sqrt(Math.Pow(values[0].X - values[1].X, 2) + Math.Pow(values[0].Y - values[1].Y, 2)));
                initialFont = consoles.Contains(control) ? settings.TerminalFontSize : settings.EditorFontSize; direction = "pinch";
            }
            control.CapturePointer(e.Pointer); e.Handled = true;
        }), true);
        control.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!pointers.ContainsKey(e.Pointer.PointerId)) return;
            Point point = e.GetCurrentPoint(control).Position; pointers[e.Pointer.PointerId] = point;
            if (pointers.Count == 2)
            {
                var values = pointers.Values.ToArray(); double current = Math.Sqrt(Math.Pow(values[0].X - values[1].X, 2) + Math.Pow(values[0].Y - values[1].Y, 2));
                double size = Math.Clamp(initialFont * current / distance, 9, consoles.Contains(control) ? 32 : 40);
                control.Editor.StyleSetSizeFractional(32, (int)(size * 100));
                // Apply to all token styles, otherwise highlighted words retain their old size.
                for (int style = 0; style <= 39; style++) control.Editor.StyleSetSizeFractional(style, (int)(size * 100));
                if (consoles.Contains(control)) settings.TerminalFontSize = Math.Clamp(size, 9, 32); else settings.EditorFontSize = size;
                e.Handled = true; return;
            }
            if (direction == "pinch") { e.Handled = true; return; }
            double dx = point.X - origin.X, dy = point.Y - origin.Y;
            if (direction.Length == 0 && Math.Sqrt(dx * dx + dy * dy) >= 8) { direction = "scroll"; hold.Stop(); }
            double scale = control.XamlRoot.RasterizationScale;
            if (direction == "scroll")
            {
                PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, dx, dy);
                double milliseconds = Math.Max(1, (DateTime.UtcNow - lastMove).TotalMilliseconds);
                velocity = (previous.Y - point.Y) * scale * 16 / milliseconds;
            }
            previous = point; lastMove = DateTime.UtcNow; e.Handled = true;
        }), true);
        control.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!pointers.Remove(e.Pointer.PointerId)) return; hold.Stop();
            control.ReleasePointerCapture(e.Pointer);
            if (pointers.Count == 0)
            {
                if (direction.Length == 0) { control.Editor.SetEmptySelection(anchor); control.Editor.ScrollCaret(); selectionChanged?.Invoke(); }
                if (direction == "scroll") PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, previous.X - origin.X, previous.Y - origin.Y);
                if (direction == "scroll" && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) inertia.Start();
                if (direction == "pinch") _ = SafeAsync(async () => { ApplyAppearance(); await store.SaveAsync(settings); });
            }
            e.Handled = true;
        }), true);
        control.PointerCanceled += (_, e) => { pointers.Remove(e.Pointer.PointerId); hold.Stop(); inertia.Stop(); };
        control.Unloaded += (_, _) => { hold.Stop(); inertia.Stop(); pointers.Clear(); };
    }
}

