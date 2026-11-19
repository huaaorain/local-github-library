using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GitHubTopicReports;

public sealed class FavoriteDragGhostAdorner : Adorner
{
    private const double GhostWidth = 300;
    private const double GhostHeight = 52;

    private readonly string _repositoryName;
    private Point _position;

    public FavoriteDragGhostAdorner(FrameworkElement adornedElement, string repositoryName)
        : base(adornedElement)
    {
        _repositoryName = repositoryName;
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
        var maxLeft = Math.Max(0, AdornedElement.RenderSize.Width - GhostWidth);
        var maxTop = Math.Max(0, AdornedElement.RenderSize.Height - GhostHeight);
        _position = new Point(
            Math.Clamp(pointerPosition.X + 14, 0, maxLeft),
            Math.Clamp(pointerPosition.Y + 12, 0, maxTop));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(_position, new Size(GhostWidth, GhostHeight));
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
            _repositoryName,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            13,
            primary,
            pixelsPerDip)
        {
            MaxTextWidth = GhostWidth - 48,
            MaxTextHeight = 18,
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawingContext.DrawText(
            title,
            new Point(_position.X + 36, _position.Y + 9));

        var subtitle = new FormattedText(
            "拖动以调整收藏顺序",
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
