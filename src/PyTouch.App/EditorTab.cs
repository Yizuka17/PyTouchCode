using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PyTouch.Core;
using WinUIEditor;
namespace PyTouch.App;
internal sealed class EditorTab
{
    public required Document Document { get; init; }
    public required EditorBaseControl Control { get; init; }
    public required TabViewItem Item { get; init; }
    public required Grid View { get; init; }
    public required TextBlock Ghost { get; init; }
    public required Canvas Handles { get; init; }
    public required Border StartHandle { get; init; }
    public required Border EndHandle { get; init; }
    public bool ShowHandles { get; set; }
    public bool Loading { get; set; }
    public bool Initialized { get; set; }
    public int Revision { get; set; }
    public CancellationTokenSource? CompletionCancellation { get; set; }
    public DispatcherTimer CompletionTimer { get; } = new() { Interval = TimeSpan.FromMilliseconds(350) };
    public string GhostInsertion { get; set; } = "";
    public string? IconKey { get; set; }
    public HashSet<int> BreakpointHandles { get; } = [];
}

