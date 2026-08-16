using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OsuMate.Utils
{
  public static class ScrollBarClickToPositionBehavior
  {
    public static void Register()
    {
      EventManager.RegisterClassHandler(
        typeof(ScrollBar),
        UIElement.PreviewMouseLeftButtonDownEvent,
        new MouseButtonEventHandler(OnScrollBarPreviewMouseDown)
      );
    }

    private static void OnScrollBarPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
      if (sender is not ScrollBar scrollBar)
        return;

      if (e.OriginalSource is Thumb)
        return;
      if (IsChildOfThumb(e.OriginalSource as DependencyObject))
        return;

      var track = GetDescendant<Track>(scrollBar);
      if (track == null)
        return;

      double clickPos;
      double trackLength;
      double thumbLength;

      if (scrollBar.Orientation == Orientation.Vertical)
      {
        clickPos = e.GetPosition(track).Y;
        trackLength = track.ActualHeight;
        thumbLength = track.Thumb?.ActualHeight ?? 0;
      }
      else
      {
        clickPos = e.GetPosition(track).X;
        trackLength = track.ActualWidth;
        thumbLength = track.Thumb?.ActualWidth ?? 0;
      }

      double usableLength = trackLength - thumbLength;
      if (usableLength <= 0)
        return;

      double ratio = (clickPos - thumbLength / 2.0) / usableLength;
      ratio = Math.Max(0, Math.Min(1, ratio));

      double newValue = scrollBar.Minimum + ratio * (scrollBar.Maximum - scrollBar.Minimum);

      var scrollViewer = FindAncestor<ScrollViewer>(scrollBar);
      if (scrollViewer != null)
      {
        if (scrollBar.Orientation == Orientation.Vertical)
          scrollViewer.ScrollToVerticalOffset(newValue);
        else
          scrollViewer.ScrollToHorizontalOffset(newValue);
      }
      else
      {
        scrollBar.Value = newValue;
      }

      e.Handled = true;
    }

    private static bool IsChildOfThumb(DependencyObject? obj)
    {
      while (obj != null)
      {
        if (obj is Thumb)
          return true;
        obj = VisualTreeHelper.GetParent(obj);
      }
      return false;
    }

    private static T? GetDescendant<T>(DependencyObject parent)
      where T : DependencyObject
    {
      int count = VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++)
      {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T result)
          return result;
        var found = GetDescendant<T>(child);
        if (found != null)
          return found;
      }
      return null;
    }

    private static T? FindAncestor<T>(DependencyObject obj)
      where T : DependencyObject
    {
      var parent = VisualTreeHelper.GetParent(obj);
      while (parent != null)
      {
        if (parent is T result)
          return result;
        parent = VisualTreeHelper.GetParent(parent);
      }
      return null;
    }
  }
}
