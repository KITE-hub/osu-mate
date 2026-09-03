using System.Threading;
using System.Windows.Threading;
using OsuMate.ViewModels;

namespace OsuMate.Services
{
  public sealed class KeyOverlayUiThread : IDisposable
  {
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher _dispatcher = null!;
    private Views.KeyOverlayWindow _window = null!;
    private Exception? _initException;

    public event Action<double, double>? PositionChanged;
    public event Action<double>? FlowLengthChanged;

    public KeyOverlayUiThread(KeyOverlayViewModel vm)
    {
      _thread = new Thread(() => Run(vm)) { IsBackground = true };
      _thread.SetApartmentState(ApartmentState.STA);
      _thread.Start();
      _ready.Wait();
      if (_initException != null)
        throw new InvalidOperationException(
          "Failed to initialize key overlay UI thread",
          _initException
        );
    }

    private void Run(KeyOverlayViewModel vm)
    {
      try
      {
        _window = new Views.KeyOverlayWindow(vm);
        _window.PositionChanged += (left, top) => PositionChanged?.Invoke(left, top);
        _window.FlowLengthChanged += length => FlowLengthChanged?.Invoke(length);
        _dispatcher = _window.Dispatcher;
      }
      catch (Exception ex)
      {
        _initException = ex;
        _ready.Set();
        return;
      }
      _ready.Set();
      Dispatcher.Run();
    }

    public void Show() => _dispatcher.Invoke(() => _window.Show());

    public void Hide() => _dispatcher.Invoke(() => _window.Hide());

    public void SetDraggable(bool draggable) =>
      _dispatcher.Invoke(() => _window.SetDraggable(draggable));

    public void UpdateSettings(
      int rotation,
      double flowLength,
      double speed,
      double round,
      double laneWidth
    ) =>
      _dispatcher.Invoke(
        () =>
          _window.UpdateSettings(rotation, flowLength, speed, round, laneWidth)
      );

    public void SetPosition(double left, double top) =>
      _dispatcher.Invoke(() =>
      {
        _window.Left = left;
        _window.Top = top;
      });

    public void ApplyPositionIfIdle(double left, double top) =>
      _dispatcher.BeginInvoke(() =>
      {
        if (!_window.IsVisible || _window.IsDragging || _window.IsResizing)
          return;
        _window.Left = left;
        _window.Top = top;
      });

    public void Dispose()
    {
      _dispatcher.InvokeShutdown();
      _thread.Join(TimeSpan.FromSeconds(2));
      _ready.Dispose();
    }
  }
}
