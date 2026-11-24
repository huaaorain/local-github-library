using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GitHubTopicReports;

public sealed class NoteTreeActionGutterAdorner : Adorner
{
    private static readonly Geometry TrashGeometry = Geometry.Parse(
        "M 2,4.5 H 14 L 13,15 H 3 Z M 5.5,4.5 L 6,1.5 H 10 L 10.5,4.5 Z");

    private readonly TreeView _tree;

    public NoteTreeActionGutterAdorner(TreeView tree)
        : base(tree)
    {
        _tree = tree;
        IsHitTestVisible = false;
    }

    public double ActionCenterX
    {
        get
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(_tree);
            var viewportRight = scrollViewer is null
                ? _tree.ActualWidth - 8
                : scrollViewer.TranslatePoint(
                    new Point(scrollViewer.ViewportWidth, 0),
                    _tree).X;
            return Math.Max(18, viewportRight - 16);
        }
    }

    public int LastRenderedCount { get; private set; }
    public int RenderPassCount { get; private set; }

    public void Refresh() => InvalidateVisual();

    public TreeViewItem? FindContainerAt(double y)
    {
        foreach (var container in EnumerateTreeContainers(_tree))
        {
            if (container.Visibility != Visibility.Visible)
            {
                continue;
            }

            var row = FindVisualChild<FrameworkElement>(
                container,
                element => element.Name == "NoteTreeRow");
            if (row is null)
            {
                continue;
            }

            var top = row.TranslatePoint(new Point(0, 0), _tree).Y;
            if (y >= top && y <= top + row.ActualHeight)
            {
                return container;
            }
        }

        return null;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        RenderPassCount++;
        base.OnRender(drawingContext);
        var dangerColor = Application.Current.TryFindResource("DangerBrush")
            is SolidColorBrush danger
            ? danger.Color
            : Color.FromRgb(222, 104, 104);
        var fill = new SolidColorBrush(Color.FromArgb(
            128,
            dangerColor.R,
            dangerColor.G,
            dangerColor.B));
        var x = ActionCenterX - 7;
        var renderedCount = 0;
        foreach (var container in EnumerateTreeContainers(_tree))
        {
            if (container.Visibility != Visibility.Visible)
            {
                continue;
            }

            var row = FindVisualChild<FrameworkElement>(
                container,
                element => element.Name == "NoteTreeRow");
            if (row is null)
            {
                continue;
            }

            var top = row.TranslatePoint(new Point(0, 0), _tree).Y;
            if (top + row.ActualHeight < 0 || top > _tree.ActualHeight)
            {
                continue;
            }

            var y = top + (row.ActualHeight - 14) / 2;
            var translatedGeometry = TrashGeometry.Clone();
            translatedGeometry.Transform = new TranslateTransform(x, y);
            translatedGeometry.Freeze();
            drawingContext.DrawGeometry(fill, null, translatedGeometry);
            renderedCount++;
        }
        LastRenderedCount = renderedCount;
    }

    private static IEnumerable<TreeViewItem> EnumerateTreeContainers(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item)
                is not TreeViewItem container)
            {
                continue;
            }

            yield return container;
            if (!container.IsExpanded)
            {
                continue;
            }

            foreach (var child in EnumerateTreeContainers(container))
            {
                yield return child;
            }
        }
    }

    private static T? FindVisualChild<T>(
        DependencyObject parent,
        Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            if (FindVisualChild(child, predicate) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
