using System;
using OsuMate.ViewModels;
using OsuMate.Views;

namespace OsuMate.Services.Key;

public sealed class KeyOverlayUiThread : IDisposable
{
  private readonly KeyOverlayDirectXWindow _window;

  public event Action<double, double>? PositionChanged
  {
    add => _window.PositionChanged += value;
    remove => _window.PositionChanged -= value;
  }

  public event Action<double>? FlowLengthChanged
  {
    add => _window.FlowLengthChanged += value;
    remove => _window.FlowLengthChanged -= value;
  }

  public KeyOverlayUiThread(KeyOverlayViewModel vm)
  {
    _window = new KeyOverlayDirectXWindow(vm);
  }

  public void Show() => _window.Show();

  public void Hide() => _window.Hide();

  public void SetDraggable(bool draggable) => _window.SetDraggable(draggable);

  public void UpdateSettings(
    int rotation,
    double flowLength,
    double durationMs,
    double round,
    double laneWidth,
    string? fontFamily = null,
    double inputBarOpacity = 0.5,
    double beatmapBarOpacity = 0.5,
    double beatmapTapLengthMs = 25
  ) => _window.UpdateSettings(rotation, flowLength, durationMs, round, laneWidth, fontFamily, inputBarOpacity, beatmapBarOpacity, beatmapTapLengthMs);

  public void SetPosition(double left, double top) => _window.SetPosition(left, top);

  public void ApplyPositionIfIdle(double left, double top) => _window.ApplyPositionIfIdle(left, top);

  public void Dispose() => _window.Dispose();
}
