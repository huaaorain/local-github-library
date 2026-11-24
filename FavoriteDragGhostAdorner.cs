using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GitHubTopicReports;

public sealed class FavoriteDragGhostAdorner : Adorner
{
    private const double GhostHeight = 52;

    private readonly string _subtitle;
    private readonly string _title;
    private readonly double _width;
    private readonly bool _centerOnPointer;
    private Point _position;

    public FavoriteDragGhostAdorner(
        FrameworkElement adornedElement,
        string title,
        string subtitle = "拖动以调整收藏顺序",
        double width = 300,
        bool centerOnPointer = false)
        : base(adornedElement)
    {
        _title = title;
        _subtitle = subtitle;
        _width = width;
        _centerOnPointer = centerOnPointer;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Effect = new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = 0.24
        };
    }

    public Point Position => _position;

    public void UpdatePosition(Point pointerPosition)
    {
        var maxLeft = Math.Max(0, AdornedElement.RenderSize.Width - _width);
        var maxTop = Math.Max(0, AdornedElement.RenderSize.Height - GhostHeight);
        var desiredX = _centerOnPointer
            ? pointerPosition.X - _width / 2
            : pointerPosition.X + 14;
        var desiredY = _centerOnPointer
            ? pointerPosition.Y - GhostHeight / 2
            : pointerPosition.Y + 12;
        _position = new Point(
            Math.Clamp(desiredX, 0, maxLeft),
            Math.Clamp(desiredY, 0, maxTop));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(_position, new Size(_width, GhostHeight));
        var background = CreateBrush("SurfaceBrush", Color.FromRgb(255, 255, 255), 0.94);
        var border = CreateBrush("PrimaryBrush", Color.FromRgb(31, 95, 191), 0.7);
        var primary = CreateBrush("TextPrimaryBrush", Color.FromRgb(20, 32, 51), 1);
        var secondary = CreateBrush("TextSecondaryBrush", Color.FromRgb(80, 97, 118), 1);
        drawingContext.DrawRoundedRectangle(
            background,
            new Pen(border, 1.2),
            bounds,
            10,
            10);

        for (var index = 0; index < 3; index++)
        {
            drawingContext.DrawRoundedRectangle(
                secondary,
                null,
                new Rect(_position.X + 14, _position.Y + 19 + index * 7, 10, 1.5),
                1,
                1);
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = new FormattedText(
            _title,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            13,
            primary,
            pixelsPerDip)
        {
            MaxTextWidth = _width - 48,
            MaxTextHeight = 18,
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawingContext.DrawText(
            title,
            new Point(_position.X + 36, _position.Y + 9));

        var subtitle = new FormattedText(
            _subtitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            11,
            secondary,
            pixelsPerDip);
        drawingContext.DrawText(
            subtitle,
            new Point(_position.X + 36, _position.Y + 29));
    }

    private static SolidColorBrush CreateBrush(
        string resourceKey,
        Color fallback,
        double opacity)
    {
        var color = Application.Current.TryFindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : fallback;
        return new SolidColorBrush(color) { Opacity = opacity };
    }
}
