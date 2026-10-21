using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    InputPane? touchKeyboard;
    bool touchKeyboardInitialized;
    WinUIEditor.EditorBaseControl? touchKeyboardGestureOwner;
    void EnsureTouchKeyboardGuard()
    {
        if (touchKeyboardInitialized) return;
        touchKeyboardInitialized = true;
        try
        {
            touchKeyboard = InputPaneInterop.GetForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            touchKeyboard.Showing += (_, _) => HideGestureKeyboard();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or NotSupportedException) { }
    }
    bool ShouldBlockGestureKeyboard()
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        var editors = documents.Select(t => t.Control).Concat(consoles).ToArray();
        return editors.Any(c => ReferenceEquals(c, focused)) && touchKeyboardGestureOwner is { } owner && touchInputs.TryGetValue(owner, out var gate) && gate.BlockKeyboard();
    }
    void HideGestureKeyboard()
    {
        if (!ShouldBlockGestureKeyboard()) return;
        try { touchKeyboard?.TryHide(); }
        catch (Exception e) when (e is COMException or InvalidOperationException) { }
    }
}
