using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GitHubTopicReports;

public sealed class DataGridColumnResizeThumb : Thumb
{
    public static event Action? Resized;

    public DataGridColumnResizeThumb()
    {
        DragDelta += OnDragDelta;
        DragCompleted += (_, _) => Resized?.Invoke();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var header = TemplatedParent as DataGridColumnHeader
                     ?? FindVisualAncestor<DataGridColumnHeader>(this);
        var column = header?.Column;
        if (column is null || !column.CanUserResize)
        {
            return;
        }

        var minimum = Math.Max(28, column.MinWidth);
        var maximum = column.MaxWidth > 0
            ? column.MaxWidth
            : double.PositiveInfinity;
        var width = Math.Clamp(
            column.ActualWidth + e.HorizontalChange,
            minimum,
            maximum);
        column.Width = new DataGridLength(width);
    }

    private static T? FindVisualAncestor<T>(DependencyObject current)
        where T : DependencyObject
    {
        while (current is not null && current is not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
