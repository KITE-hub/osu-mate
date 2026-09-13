using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public sealed class KeyOverlaySettingsViewModel : ObservableBase
  {
    public event Action? OnSaveKeyOverlayPositionRequested;
    public event Action? OnApplyKeyOverlayPositionRequested;
    public event Action? OnSaveKeyOverlayFlowLengthRequested;
    public event Action? OnApplyKeyOverlayFlowLengthRequested;

    public void RequestSaveKeyOverlayPosition() => OnSaveKeyOverlayPositionRequested?.Invoke();

    public void RequestApplyKeyOverlayPosition() => OnApplyKeyOverlayPositionRequested?.Invoke();

    public void RequestSaveKeyOverlayFlowLength() => OnSaveKeyOverlayFlowLengthRequested?.Invoke();

    public void RequestApplyKeyOverlayFlowLength() => OnApplyKeyOverlayFlowLengthRequested?.Invoke();

    private readonly Func<PresetConfig> _presetConfig;
    private readonly Action _save;
    private readonly Action _debouncedSave;

    public KeyOverlaySettingsViewModel(
      Func<PresetConfig> presetConfig,
      Action save,
      Action debouncedSave
    )
    {
      _presetConfig = presetConfig;
      _save = save;
      _debouncedSave = debouncedSave;
    }

    public bool KeyOverlayEnabled
    {
      get => _presetConfig().KeyOverlayEnabled;
      set
      {
        _presetConfig().KeyOverlayEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }

    public int KeyOverlayRotation
    {
      get => _presetConfig().KeyOverlayRotation;
      set
      {
        _presetConfig().KeyOverlayRotation = MathUtils.SnapTo90Degrees(value);
        OnPropertyChanged();
        OnPropertyChanged(nameof(KeyOverlayRotationLabel));
        OnPropertyChanged(nameof(KeyOverlaySizeText));
        _save();
      }
    }

    public string KeyOverlayRotationLabel => $"{KeyOverlayRotation}°";

    public double KeyOverlayLaneWidth
    {
      get => _presetConfig().KeyOverlayLaneWidth;
      set
      {
        _presetConfig().KeyOverlayLaneWidth = Math.Clamp(value, 25, 100);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double KeyOverlayHeight
    {
      get => _presetConfig().KeyOverlayHeight;
      set
      {
        _presetConfig().KeyOverlayHeight = Math.Max(120, value);
        OnPropertyChanged();
        OnPropertyChanged(nameof(KeyOverlaySizeText));
        _debouncedSave();
      }
    }

    public string KeyOverlaySizeText => KeyOverlayRotation is 90 or 270
      ? $"W: {(int)KeyOverlayHeight}"
      : $"H: {(int)KeyOverlayHeight}";

    public double KeyOverlayX
    {
      get => _presetConfig().KeyOverlayX;
      set
      {
        _presetConfig().KeyOverlayX = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(KeyOverlayPositionText));
        _save();
      }
    }

    public double KeyOverlayY
    {
      get => _presetConfig().KeyOverlayY;
      set
      {
        _presetConfig().KeyOverlayY = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(KeyOverlayPositionText));
        _save();
      }
    }

    public string KeyOverlayPositionText => $"X: {(int)KeyOverlayX}  Y: {(int)KeyOverlayY}";

    public void SetKeyOverlayPosition(double x, double y)
    {
      _presetConfig().KeyOverlayX = x;
      _presetConfig().KeyOverlayY = y;
      OnPropertyChanged(nameof(KeyOverlayX));
      OnPropertyChanged(nameof(KeyOverlayY));
      OnPropertyChanged(nameof(KeyOverlayPositionText));
      _save();
    }

    public double KeyOverlayDurationMs
    {
      get => _presetConfig().KeyOverlayDurationMs <= 0 ? 1000 : _presetConfig().KeyOverlayDurationMs;
      set
      {
        _presetConfig().KeyOverlayDurationMs = Math.Round(Math.Clamp(value, 200, 1500) / 10) * 10;
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double KeyOverlayBarRound
    {
      get => _presetConfig().KeyOverlayBarRound;
      set
      {
        _presetConfig().KeyOverlayBarRound = Math.Clamp(value, 0, 32);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double KeyOverlayInputBarOpacity
    {
      get => _presetConfig().KeyOverlayInputBarOpacity;
      set
      {
        _presetConfig().KeyOverlayInputBarOpacity = Math.Clamp(value, 0.0, 1.0);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public void NotifyPresetApplied()
    {
      OnPropertyChanged(nameof(KeyOverlayEnabled));
      OnPropertyChanged(nameof(KeyOverlayRotation));
      OnPropertyChanged(nameof(KeyOverlayRotationLabel));
      OnPropertyChanged(nameof(KeyOverlayLaneWidth));
      OnPropertyChanged(nameof(KeyOverlayHeight));
      OnPropertyChanged(nameof(KeyOverlaySizeText));
      OnPropertyChanged(nameof(KeyOverlayX));
      OnPropertyChanged(nameof(KeyOverlayY));
      OnPropertyChanged(nameof(KeyOverlayPositionText));
      OnPropertyChanged(nameof(KeyOverlayDurationMs));
      OnPropertyChanged(nameof(KeyOverlayBarRound));
      OnPropertyChanged(nameof(KeyOverlayInputBarOpacity));
    }
  }
}
