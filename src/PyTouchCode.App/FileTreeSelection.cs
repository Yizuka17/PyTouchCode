using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    float? indicatorY;
    TreeViewNode? indicatorNode;
    bool indicatorPending;
    void FileTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        indicatorPending = true;
        DispatcherQueue.TryEnqueue(() => { UpdateFileIndicator(true); indicatorPending = false; });
    }
    void FileTreeLayoutUpdated(object? sender, object args)
    {
        if (!indicatorPending) UpdateFileIndicator(false);
    }
    void UpdateFileIndicator(bool animate)
    {
        if (FileSelectionIndicator is null || FileTree.SelectedNode is not { } node || FileTree.ContainerFromNode(node) is not FrameworkElement row || row.ActualHeight == 0)
        { if (FileSelectionIndicator is not null) FileSelectionIndicator.Opacity = 0; indicatorY = null; return; }
        double y = row.TransformToVisual(FileTreeHost).TransformPoint(new Windows.Foundation.Point()).Y + (row.ActualHeight - 16) / 2;
        if (y < 0 || y + 16 > FileTreeHost.ActualHeight) { FileSelectionIndicator.Opacity = 0; indicatorY = null; return; }
        bool selectionChanged = !ReferenceEquals(indicatorNode, node);
        float next = (float)y;
        if (indicatorY == next) return;
        ElementCompositionPreview.SetIsTranslationEnabled(FileSelectionIndicator, true);
        var visual = ElementCompositionPreview.GetElementVisual(FileSelectionIndicator);
        bool motion = (animate || selectionChanged) && indicatorY.HasValue && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        FileSelectionIndicator.Opacity = 1;
        if (motion)
        {
            // NavigationView's native stretch-then-contract selection motion:
            // microsoft/microsoft-ui-xaml, NavigationView::PlayIndicatorAnimations.
            float from = indicatorY!.Value;
            var compositor = visual.Compositor;
            var step = compositor.CreateStepEasingFunction(); step.IsFinalStepSingleFrame = true;
            var stretch = compositor.CreateCubicBezierEasingFunction(new Vector2(.9f, .1f), new Vector2(1, .2f));
            var settle = compositor.CreateCubicBezierEasingFunction(new Vector2(.1f, .9f), new Vector2(.2f, 1));
            visual.StopAnimation("Translation.Y"); visual.StopAnimation("Scale.Y"); visual.StopAnimation("CenterPoint.Y");
            visual.Scale = Vector3.One;
            var position = compositor.CreateScalarKeyFrameAnimation();
            position.InsertKeyFrame(0, from); position.InsertKeyFrame(1f / 3, next, step); position.InsertKeyFrame(1, next, step);
            position.Duration = TimeSpan.FromMilliseconds(600);
            var scale = compositor.CreateScalarKeyFrameAnimation();
            scale.InsertKeyFrame(0, 1); scale.InsertKeyFrame(1f / 3, Math.Abs(next - from) / 16 + 1, stretch); scale.InsertKeyFrame(1, 1, settle);
            scale.Duration = TimeSpan.FromMilliseconds(600);
            var center = compositor.CreateScalarKeyFrameAnimation();
            center.InsertKeyFrame(0, from < next ? 0 : 16); center.InsertKeyFrame(1, from < next ? 16 : 0, step);
            center.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Translation.Y", position); visual.StartAnimation("Scale.Y", scale); visual.StartAnimation("CenterPoint.Y", center);
        }
        else
        {
            visual.StopAnimation("Translation.Y"); visual.StopAnimation("Scale.Y"); visual.StopAnimation("CenterPoint.Y");
            visual.Scale = Vector3.One; visual.CenterPoint = Vector3.Zero;
            visual.Properties.InsertVector3("Translation", new Vector3(0, next, 0));
        }
        indicatorNode = node;
        indicatorY = next;
    }
}
