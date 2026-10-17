using OsuMate.Utils;
using OsuMate.ViewModels;
using OsuMate.Views;

namespace OsuMate.Services.Key
{

  public sealed class KeyOverlayWindowController : IDisposable
  {
    private readonly KeyOverlayDirectXWindow? _window;

    public event Action<double, double>? PositionChanged
    {
      add
      {
        if (_window != null)
          _window.PositionChanged += value;
      }
      remove
      {
        if (_window != null)
          _window.PositionChanged -= value;
      }
    }

    public event Action<double>? FlowLengthChanged
    {
      add
      {
        if (_window != null)
          _window.FlowLengthChanged += value;
      }
      remove
      {
        if (_window != null)
          _window.FlowLengthChanged -= value;
      }
    }

    public KeyOverlayWindowController(KeyOverlayViewModel vm)
    {
      try
      {
        _window = new KeyOverlayDirectXWindow(vm);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger(
          "KeyOverlayWindowController: key overlay disabled because initialization failed: " + e,
          true,
          writeToFile: true
        );
      }
    }

    public void Show() => _window?.Show();

    public void Hide() => _window?.Hide();

    public void SetDraggable(bool draggable) => _window?.SetDraggable(draggable);

    public void UpdateSettings(
      int rotation,
      double flowLength,
      double durationMs,
      double round,
      double laneWidth,
      string? fontFamily = null,
      double inputBarOpacity = 0.5
    ) => _window?.UpdateSettings(rotation, flowLength, durationMs, round, laneWidth, fontFamily, inputBarOpacity);

    public void SetPosition(double left, double top) => _window?.SetPosition(left, top);

    public void ApplyPositionIfIdle(double left, double top) => _window?.ApplyPositionIfIdle(left, top);

    public void Dispose() => _window?.Dispose();
  }
}
