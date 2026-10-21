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
        ApplyWindowMaterial(settings.Material, settings.MaterialTransparency);
        if (!settings.UseSystemAccent) { var color = ParseColor(settings.Accent); RunButton.Background = new SolidColorBrush(color); }
        else RunButton.ClearValue(Control.BackgroundProperty);
        foreach (var tab in documents) { ConfigureEditor(tab.Control, false); UpdateTab(tab); }
        foreach (var console in consoles) if (console is not null) ConfigureEditor(console, true);
    }
    void ApplyWindowMaterial(string material, double transparency)
    {
        try
        {
            if (SystemBackdrop is AdjustableBackdrop existing && existing.Material == material) existing.Transparency = transparency;
            else SystemBackdrop = material switch
            {
                "Mica" or "MicaAlt" when Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() => new AdjustableBackdrop(material, transparency),
                "Acrylic" when Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() => new AdjustableBackdrop(material, transparency),
                _ => null
            };
        }
        catch (System.Runtime.InteropServices.COMException) { SystemBackdrop = null; }
        Root.Background = SystemBackdrop is null ? (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
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
            ApplyMenuHeaderFont(menu);
            Visit(menu.Items);
        }
    }
    static Button? MenuHeaderButton(DependencyObject element)
    {
        if (element is Button { Name: "ContentButton" } button) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (MenuHeaderButton(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
    void ApplyMenuHeaderFont(MenuBarItem menu)
    {
        // WinUI's MenuBarItem template does not bind its internal button's font.
        menu.ApplyTemplate();
        if (MenuHeaderButton(menu) is { } header)
            SetUiFont(header, new FontFamily(settings.UiFont), settings.UiFontSize, settings.UiFontWeight);
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
        long margin = Enumerable.Range(0, 5).Sum(i => S(control, 2243, (ulong)i));
        long position = control.Editor.PositionFromPoint((int)Math.Max(margin + 1, point.X * scale), (int)(point.Y * scale));
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
    sealed class TouchInputGate
    {
        public FrameworkElement? Surface;
        public required Action<uint, Point> Press, Move;
        public required Action<uint> Release, Cancel;
        public required Action ResumeInput;
        public required Func<bool> BlockKeyboard;
    }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorBaseControl, TouchInputGate> touchInputs = new();
    static FrameworkElement? EditorInputSurface(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: "ImageTarget" } surface) return surface;
            if (EditorInputSurface(child) is { } found) return found;
        }
        return null;
    }
    void CommitTouchTap(EditorBaseControl control, Point point, long position, Action? selectionChanged)
    {
        if (TryToggleInlineFold(control, point)) return;
        double x = point.X * (control.XamlRoot?.RasterizationScale ?? 1);
        long edge = 0;
        for (int margin = 0; margin < 5; margin++)
        {
            edge += S(control, 2243, (ulong)margin);
            if (x >= edge) continue;
            long line = control.Editor.LineFromPosition(position);
            if (margin == 1 && documents.FirstOrDefault(t => t.Control == control) is { } tab)
                _ = SafeAsync(() => ToggleBreakpointAsync(tab, (int)line));
            return;
        }
        control.Editor.SetEmptySelection(position); control.Editor.ScrollCaret();
        touchKeyboardGestureOwner = null;
        control.Focus(FocusState.Pointer);
        control.Editor.Focus = true;
        selectionChanged?.Invoke();
    }
    void AttachTouch(EditorBaseControl control, Action? selectionChanged = null)
    {
        var pointers = new Dictionary<uint, Point>();
        bool inputSuspended = false;
        DateTime keyboardBlockedUntil = DateTime.MinValue;
        void ResumeInput()
        {
            if (pointers.Count > 0) return;
            inputSuspended = false;
            keyboardBlockedUntil = DateTime.MinValue;
            touchKeyboardGestureOwner = null;
            if (control.FocusState != FocusState.Unfocused) control.Editor.Focus = true;
        }
        control.PreviewKeyDown += (_, _) => ResumeInput();
        control.GettingFocus += (_, e) =>
        {
            if (pointers.Count > 0 && e.FocusState != FocusState.Keyboard)
            {
                e.Cancel = true; e.Handled = true;
            }
        };
        control.GotFocus += (_, _) => { if (inputSuspended) control.Editor.Focus = false; };
        Point origin = default, previous = default;
        Point multiOrigin = default;
        long anchor = 0, firstLine = 0, originalAnchor = 0, originalCaret = 0, xOffset = 0;
        string direction = "";
        double distance = 0, initialFont = 0, velocity = 0;
        bool zooming = false;
        DateTime lastMove = DateTime.UtcNow;
        var inertia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var multiFrame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        void MultiMove()
        {
            multiFrame.Stop();
            if (pointers.Count != 2) return;
            var values = pointers.Values.ToArray();
            double current = Math.Sqrt(Math.Pow(values[0].X - values[1].X, 2) + Math.Pow(values[0].Y - values[1].Y, 2));
            var center = new Point((values[0].X + values[1].X) / 2, (values[0].Y + values[1].Y) / 2);
            if (Math.Abs(current - distance) / distance > .05) zooming = true;
            if (zooming)
            {
                double size = Math.Clamp(initialFont * current / distance, 9, consoles.Contains(control) ? 32 : 40);
                for (int style = 0; style <= 39; style++) control.Editor.StyleSetSizeFractional(style, (int)(size * 100));
                if (consoles.Contains(control)) settings.TerminalFontSize = size; else settings.EditorFontSize = size;
            }
            PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, center.X - multiOrigin.X, center.Y - multiOrigin.Y);
        }
        multiFrame.Tick += (_, _) => MultiMove();
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
                item.Click += (_, _) => { var ed = control.Editor; switch (command) { case "select": ed.SetSel(ed.WordStartPosition(anchor, true), ed.WordEndPosition(anchor, true)); selectionChanged?.Invoke(); break; case "copy": ed.Copy(); break; case "cut": if (!ed.ReadOnly) { ed.Cut(); control.Focus(FocusState.Pointer); ResumeInput(); } break; case "paste": if (!ed.ReadOnly) { ed.Paste(); control.Focus(FocusState.Pointer); ResumeInput(); } break; case "selectall": ed.SelectAll(); selectionChanged?.Invoke(); break; } };
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
        void Press(uint id, Point point)
        {
            // XAML focus can stay on the editor after the keyboard was dismissed.
            // SCI_SETFOCUS also detaches its TSF document, unlike GettingFocus alone.
            inputSuspended = true;
            touchKeyboardGestureOwner = control;
            foreach (var editor in documents.Select(t => t.Control).Concat(consoles))
                if (editor.IsLoaded && editor.Editor.Focus) editor.Editor.Focus = false;
            inertia.Stop(); pointers[id] = point; hold.Stop();
            EnsureTouchKeyboardGuard();
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
                multiOrigin = new Point((values[0].X + values[1].X) / 2, (values[0].Y + values[1].Y) / 2);
                firstLine = control.Editor.FirstVisibleLine; xOffset = control.Editor.XOffset;
                PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, 0, 0);
                initialFont = consoles.Contains(control) ? settings.TerminalFontSize : settings.EditorFontSize; direction = "multi"; zooming = false;
                HideGestureKeyboard();
            }
            else if (pointers.Count > 2) { multiFrame.Stop(); direction = "multi"; }
        }
        void Move(uint id, Point point)
        {
            if (!pointers.ContainsKey(id)) return;
            if (pointers[id].Equals(point)) return;
            pointers[id] = point;
            if (pointers.Count == 2)
            {
                // Pointer updates arrive one finger at a time. Evaluate both
                // contacts in one frame so parallel movement cannot look like a pinch.
                if (!multiFrame.IsEnabled) multiFrame.Start();
                return;
            }
            if (direction == "multi") return;
            double dx = point.X - origin.X, dy = point.Y - origin.Y;
            if (direction.Length == 0 && Math.Sqrt(dx * dx + dy * dy) >= 8) { direction = Math.Abs(dx) > Math.Abs(dy) * 1.25 ? "select" : "scroll"; hold.Stop(); HideGestureKeyboard(); }
            double scale = control.XamlRoot.RasterizationScale;
            if (direction == "scroll")
            {
                PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, 0, dy);
                double milliseconds = Math.Max(1, (DateTime.UtcNow - lastMove).TotalMilliseconds);
                velocity = (previous.Y - point.Y) * scale * 16 / milliseconds;
            }
            else if (direction == "select")
            {
                control.Editor.SetSel(anchor, TouchPosition(control, point));
                S(control, 2613, (ulong)firstLine); control.Editor.XOffset = (int)xOffset;
                selectionChanged?.Invoke();
            }
            previous = point; lastMove = DateTime.UtcNow;
        }
        void Release(uint id)
        {
            if (pointers.Count == 2) MultiMove();
            if (!pointers.Remove(id)) return; hold.Stop();
            if (pointers.Count == 0)
            {
                keyboardBlockedUntil = DateTime.UtcNow.AddMilliseconds(600);
                if (direction.Length == 0) { inputSuspended = false; keyboardBlockedUntil = DateTime.MinValue; CommitTouchTap(control, origin, anchor, selectionChanged); }
                if (direction == "scroll") PanTouch(control, originalAnchor, originalCaret, firstLine, xOffset, 0, previous.Y - origin.Y);
                if (direction == "scroll" && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) inertia.Start();
                if (direction == "multi" && zooming) _ = SafeAsync(async () => { ApplyAppearance(); await store.SaveAsync(settings); });
            }
        }
        void Cancel(uint id) { if (!pointers.Remove(id)) return; hold.Stop(); inertia.Stop(); multiFrame.Stop(); direction = "multi"; keyboardBlockedUntil = DateTime.UtcNow.AddMilliseconds(600); }
        var gate = new TouchInputGate { Press = Press, Move = Move, Release = Release, Cancel = Cancel, ResumeInput = ResumeInput, BlockKeyboard = () => inputSuspended && (pointers.Count > 0 || DateTime.UtcNow < keyboardBlockedUntil) };
        long? mouseFold = null; uint mousePointer = 0;
        touchInputs.Add(control, gate);
        void BindSurface()
        {
            // EditorBaseControl.OnPointerPressed focuses and changes the caret
            // before handlers on the control run. Consume touch on its rendering
            // child so the event never reaches that native class handler.
            if (EditorInputSurface(control) is not { } surface || ReferenceEquals(gate.Surface, surface)) return;
            gate.Surface = surface; surface.IsHoldingEnabled = false;
            surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
                {
                    if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed && HitInlineFold(control, e.GetCurrentPoint(control).Position) is { } line)
                    { mouseFold = line; mousePointer = e.Pointer.PointerId; surface.CapturePointer(e.Pointer); e.Handled = true; }
                    else ResumeInput();
                    return;
                }
                gate.Press(e.Pointer.PointerId, e.GetCurrentPoint(control).Position);
                surface.CapturePointer(e.Pointer); e.Handled = true;
            }), true);
            surface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
            {
                if (mouseFold is not null && mousePointer == e.Pointer.PointerId) { e.Handled = true; return; }
                if (!pointers.ContainsKey(e.Pointer.PointerId)) return;
                gate.Move(e.Pointer.PointerId, e.GetCurrentPoint(control).Position); e.Handled = true;
            }), true);
            surface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
            {
                if (mouseFold is { } fold && mousePointer == e.Pointer.PointerId)
                {
                    mouseFold = null;
                    if (HitInlineFold(control, e.GetCurrentPoint(control).Position) == fold) ToggleInlineFold(control, fold);
                    surface.ReleasePointerCapture(e.Pointer); e.Handled = true; return;
                }
                if (!pointers.ContainsKey(e.Pointer.PointerId)) return;
                gate.Move(e.Pointer.PointerId, e.GetCurrentPoint(control).Position);
                gate.Release(e.Pointer.PointerId); surface.ReleasePointerCapture(e.Pointer); e.Handled = true;
            }), true);
            surface.PointerCanceled += (_, e) => gate.Cancel(e.Pointer.PointerId);
            surface.PointerCaptureLost += (_, e) => { if (mousePointer == e.Pointer.PointerId) mouseFold = null; gate.Cancel(e.Pointer.PointerId); };
        }
        control.Loaded += (_, _) => BindSurface();
        control.LayoutUpdated += (_, _) => { if (gate.Surface is null || !gate.Surface.IsLoaded) BindSurface(); };
        if (control.IsLoaded) BindSurface();
        control.Unloaded += (_, _) => { hold.Stop(); inertia.Stop(); multiFrame.Stop(); pointers.Clear(); if (ReferenceEquals(touchKeyboardGestureOwner, control)) touchKeyboardGestureOwner = null; };
    }
}

