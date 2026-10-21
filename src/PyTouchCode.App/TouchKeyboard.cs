using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    [ComImport, Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IThreadInputManager
    {
        void Activate(out uint id);
        void Deactivate();
        void CreateDocumentMgr(out nint document);
        void EnumDocumentMgrs(out nint documents);
        void GetFocus(out nint document);
        void SetFocus(nint document);
    }
    IThreadInputManager? threadInput;
    nint gestureInputDocument;
    void SuspendNativeTextInput()
    {
        try
        {
            if (threadInput is null)
            {
                threadInput = (IThreadInputManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50"))!)!;
                Closed += (_, _) =>
                {
                    if (gestureInputDocument != 0) { Marshal.Release(gestureInputDocument); gestureInputDocument = 0; }
                    if (threadInput is not null) { Marshal.ReleaseComObject(threadInput); threadInput = null; }
                };
            }
            if (gestureInputDocument == 0) threadInput.CreateDocumentMgr(out gestureInputDocument);
            // The editor's null SetFocus call can leave its TSF document active.
            // Use a valid document with no input context until text input resumes.
            threadInput.SetFocus(gestureInputDocument);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or NotSupportedException) { }
    }
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
