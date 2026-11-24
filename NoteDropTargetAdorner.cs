using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GitHubTopicReports;

public sealed class NoteDropTargetAdorner : Adorner
{
    public NoteDropTargetAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var primary = Application.Current.TryFindResource("PrimaryBrush") is SolidColorBrush brush
            ? brush.Color
            : Color.FromRgb(31, 95, 191);
        var fill = new SolidColorBrush(primary) { Opacity = 0.16 };
        var pen = new Pen(new SolidColorBrush(primary), 2);
        var bounds = new Rect(
            1,
            1,
            Math.Max(0, AdornedElement.RenderSize.Width - 2),
            Math.Max(0, AdornedElement.RenderSize.Height - 2));
        drawingContext.DrawRoundedRectangle(fill, pen, bounds, 6, 6);
    }
}
