using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PyTouchCode.Core;
using Windows.Foundation;
using WinUIEditor;

namespace PyTouchCode.App;
public sealed partial class MainWindow
{
    void RequestFoldOverlay(EditorTab tab)
    {
        if (tab.FoldUpdateQueued) return;
        tab.FoldUpdateQueued = true;
        DispatcherQueue.TryEnqueue(() => { tab.FoldUpdateQueued = false; UpdateFoldOverlay(tab); });
    }
    void UpdateFoldOverlay(EditorTab tab)
    {
        var control = tab.Control; var ed = control.Editor; var layer = tab.Folds;
        if (!control.IsLoaded || control.ActualWidth < 1 || tab != Active) return;
        double scale = control.XamlRoot.RasterizationScale;
        double left = (S(control, 2243, 0) + S(control, 2243, 1)) / scale;
        layer.Clip = new RectangleGeometry { Rect = new Rect(left, 0, Math.Max(0, control.ActualWidth - left - 12), Math.Max(0, control.ActualHeight - 12)) };
        var palette = ColorSchemes.ForEditor(settings, Root.ActualTheme == ElementTheme.Dark);
        var arrowBrush = new SolidColorBrush(ParseColor(palette.BrightBlack));
        var guideBrush = new SolidColorBrush(ParseColor(MixColor(palette.Background, palette.BrightBlack, .55)));
        var headers = new HashSet<long>();
        for (long display = ed.FirstVisibleLine; display <= ed.FirstVisibleLine + ed.LinesOnScreen + 1; display++)
        {
            long line = ed.DocLineFromVisible(display);
            if (line < 0 || line >= ed.LineCount) break;
            if (((int)ed.GetFoldLevel(line) & 0x2000) != 0) headers.Add(line);
            for (long parent = ed.GetFoldParent(line); parent >= 0; parent = ed.GetFoldParent(parent))
                if (!headers.Add(parent)) break;
        }
        tab.FoldTargets.Clear(); int index = 0;
        foreach (long header in headers.Order())
        {
            double x = ed.PointXFromPosition(ed.GetLineIndentPosition(header)) / scale - 10;
            double y = ed.PointYFromPosition(ed.PositionFromLine(header)) / scale;
            double height = ed.TextHeight(header) / scale;
            bool expanded = ed.GetFoldExpanded(header);
            if (!tab.FoldEnds.TryGetValue(header, out long end))
                tab.FoldEnds[header] = end = ed.GetLastChild(header, (FoldLevel)(-1));
            double bottom = ed.PointYFromPosition(ed.GetLineEndPosition(end)) / scale + ed.TextHeight(end) / scale;
            if (layer.Children.Count <= index) layer.Children.Add(new Line());
            var guide = (Line)layer.Children[index++];
            guide.X1 = guide.X2 = x; guide.Y1 = Math.Max(0, y + height / 2 + 7); guide.Y2 = Math.Min(control.ActualHeight - 12, bottom);
            guide.Stroke = guideBrush; guide.StrokeThickness = 1;
            guide.Visibility = expanded && guide.Y2 > guide.Y1 ? Visibility.Visible : Visibility.Collapsed;
            if (layer.Children.Count <= index)
            {
                var figure = new PathFigure { StartPoint = new Point(3, 4.5), IsClosed = false };
                figure.Segments.Add(new LineSegment { Point = new Point(6, 7.5) }); figure.Segments.Add(new LineSegment { Point = new Point(9, 4.5) });
                var geometry = new PathGeometry(); geometry.Figures.Add(figure);
                layer.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            }
            var arrow = (Microsoft.UI.Xaml.Shapes.Path)layer.Children[index++];
            arrow.Stroke = arrowBrush; arrow.RenderTransform = new ScaleTransform { ScaleY = expanded ? 1 : -1, CenterY = 6 };
            Canvas.SetLeft(arrow, x - 6); Canvas.SetTop(arrow, y + (height - 12) / 2);
            arrow.Visibility = y + height > 0 && y < control.ActualHeight - 12 ? Visibility.Visible : Visibility.Collapsed;
            if (arrow.Visibility == Visibility.Visible && x >= left && x < control.ActualWidth - 12)
                tab.FoldTargets.Add((header, new Rect(x - 9, y, 18, height)));
        }
        while (layer.Children.Count > index) layer.Children.RemoveAt(layer.Children.Count - 1);
    }
    long? HitInlineFold(EditorBaseControl control, Point point)
    {
        if (documents.FirstOrDefault(t => t.Control == control) is not { } tab) return null;
        UpdateFoldOverlay(tab);
        foreach (var target in tab.FoldTargets) if (target.Bounds.Contains(point)) return target.Line;
        return null;
    }
    void ToggleInlineFold(EditorBaseControl control, long line)
    {
        control.Editor.ToggleFold(line);
        if (documents.FirstOrDefault(t => t.Control == control) is { } tab) UpdateFoldOverlay(tab);
    }
    bool TryToggleInlineFold(EditorBaseControl control, Point point)
    {
        if (HitInlineFold(control, point) is not { } line) return false;
        ToggleInlineFold(control, line); return true;
    }
}
