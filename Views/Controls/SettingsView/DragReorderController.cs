using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OsuMate.Views.Controls
{
  internal sealed class DragReorderController<T>
    where T : class
  {
    private readonly UIElement _adornerHost;
    private readonly UIElement _list;
    private readonly Func<IList<T>> _getItems;
    private readonly Action<int, int> _move;
    private readonly Dictionary<T, BitmapSource> _bitmapCache = [];

    private T? _draggingItem;
    private bool _isDragging;
    private double _clickOffsetY;
    private DragGhostAdorner? _ghost;
    private AdornerLayer? _adornerLayer;

    public DragReorderController(
      UIElement adornerHost,
      UIElement list,
      Func<IList<T>> getItems,
      Action<int, int> move
    )
    {
      _adornerHost = adornerHost;
      _list = list;
      _getItems = getItems;
      _move = move;

      _list.MouseMove += OnListMouseMove;
      _list.MouseLeftButtonUp += (_, _) => Stop();
      _list.LostMouseCapture += (_, _) => Stop();
    }

    public void InvalidateBitmapCache() => _bitmapCache.Clear();

    public void CacheAllBitmaps()
    {
      var panel = GetStackPanel();
      if (panel == null)
        return;

      for (int i = 0; i < panel.Children.Count; i++)
      {
        if (panel.Children[i] is not ContentPresenter cp)
          continue;
        if (VisualTreeHelper.GetChildrenCount(cp) == 0)
          continue;
        if (VisualTreeHelper.GetChild(cp, 0) is not Border border)
          continue;
        if (border.Tag is not T item)
          continue;
        _bitmapCache[item] = RenderToBitmap(border);
      }
    }

    public void OnItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (sender is not Border border)
        return;
      if (border.Tag is not T item)
        return;
      if (e.OriginalSource is CheckBox)
        return;

      if (_bitmapCache.Count == 0)
        CacheAllBitmaps();

      _draggingItem = item;
      _isDragging = true;
      _clickOffsetY = e.GetPosition(border).Y;

      _adornerLayer = AdornerLayer.GetAdornerLayer(_adornerHost);
      if (_adornerLayer != null)
      {
        if (!_bitmapCache.TryGetValue(item, out var bitmap))
          bitmap = RenderToBitmap(border);

        var borderPos = border.TranslatePoint(new Point(0, 0), _adornerHost);
        _ghost = new DragGhostAdorner(_adornerHost, bitmap, _clickOffsetY, borderPos.X);
        _ghost.UpdatePosition(e.GetPosition(_adornerHost));
        _adornerLayer.Add(_ghost);
      }

      border.Opacity = 0.3;
      _list.CaptureMouse();
      e.Handled = true;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
      if (!_isDragging || _draggingItem == null)
        return;
      if (e.LeftButton != MouseButtonState.Pressed)
      {
        Stop();
        return;
      }

      _ghost?.UpdatePosition(e.GetPosition(_adornerHost));

      var posInList = e.GetPosition(_list);
      int currentIndex = _getItems().IndexOf(_draggingItem);
      int targetIndex = GetIndexAtPosition(posInList, currentIndex);

      if (targetIndex >= 0 && targetIndex != currentIndex)
      {
        _move(currentIndex, targetIndex);
        _list.Dispatcher.BeginInvoke(
          CacheAllBitmaps,
          System.Windows.Threading.DispatcherPriority.Render
        );
      }
    }

    public void Stop()
    {
      if (!_isDragging)
        return;

      if (_ghost != null && _adornerLayer != null)
      {
        _adornerLayer.Remove(_ghost);
        _ghost = null;
      }

      var panel = GetStackPanel();
      if (panel != null)
      {
        foreach (var child in panel.Children.OfType<ContentPresenter>())
        {
          if (
            VisualTreeHelper.GetChildrenCount(child) > 0
            && VisualTreeHelper.GetChild(child, 0) is Border b
          )
            b.Opacity = 1.0;
        }
      }

      _draggingItem = null;
      _isDragging = false;
      _list.ReleaseMouseCapture();
    }

    private int GetIndexAtPosition(Point pos, int currentIndex)
    {
      var panel = GetStackPanel();
      if (panel == null)
        return -1;

      for (int i = 0; i < panel.Children.Count; i++)
      {
        if (panel.Children[i] is not ContentPresenter cp)
          continue;
        var transform = cp.TransformToAncestor(_list);
        var topLeft = transform.Transform(new Point(0, 0));
        double height = cp.ActualHeight;

        if (i < currentIndex && pos.Y < topLeft.Y + height * 0.75)
          return i;
        if (i > currentIndex && pos.Y > topLeft.Y + height * 0.25)
          return i;
      }
      return -1;
    }

    private static RenderTargetBitmap RenderToBitmap(Border source)
    {
      var rtb = new RenderTargetBitmap(
        (int)source.ActualWidth,
        (int)source.ActualHeight,
        96,
        96,
        PixelFormats.Pbgra32
      );
      rtb.Render(source);
      return rtb;
    }

    private StackPanel? GetStackPanel()
    {
      UIElement? current = _list;
      for (int depth = 0; depth < 6; depth++)
      {
        if (current is StackPanel sp)
          return sp;
        int count = VisualTreeHelper.GetChildrenCount(current);
        if (count == 0)
          return null;
        current = VisualTreeHelper.GetChild(current, 0) as UIElement;
      }
      return null;
    }
  }
}
