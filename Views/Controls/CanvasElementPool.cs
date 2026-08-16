using System.Windows;
using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
  public class CanvasElementPool<T>
    where T : FrameworkElement
  {
    private readonly Canvas _canvas;
    private readonly Func<T> _factory;
    private readonly List<T> _pool = [];
    private int _cursor;

    public CanvasElementPool(Canvas canvas, Func<T> factory, int prewarmCount = 0)
    {
      _canvas = canvas;
      _factory = factory;
      for (int i = 0; i < prewarmCount; i++)
        Add();
    }

    private T Add()
    {
      var element = _factory();
      element.Visibility = Visibility.Collapsed;
      _pool.Add(element);
      _canvas.Children.Add(element);
      return element;
    }

    public T Get()
    {
      var element = _cursor < _pool.Count ? _pool[_cursor] : Add();
      _cursor++;
      element.Visibility = Visibility.Visible;
      return element;
    }

    public void Reset()
    {
      for (int i = 0; i < _cursor; i++)
        _pool[i].Visibility = Visibility.Collapsed;
      _cursor = 0;
    }
  }
}
