using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class KeyOverlaySettingsViewModel : ObservableBase
  {
    public event Action? OnSaveKeyOverlayPositionRequested;
    public event Action? OnApplyKeyOverlayPositionRequested;

    public void RequestSaveKeyOverlayPosition() => OnSaveKeyOverlayPositionRequested?.Invoke();

    public void RequestApplyKeyOverlayPosition() => OnApplyKeyOverlayPositionRequested?.Invoke();

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
        var normalized = ((value % 360) + 360) % 360;
        _presetConfig().KeyOverlayRotation = (int)Math.Round(normalized / 90.0) * 90 % 360;
        OnPropertyChanged();
        OnPropertyChanged(nameof(KeyOverlayRotationLabel));
        OnPropertyChanged(nameof(KeyOverlaySizeText));
        _save();
      }
    }

    public string KeyOverlayRotationLabel => $"{KeyOverlayRotation}°";

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
    public string KeyOverlaySizeText => KeyOverlayRotation is 90 or 270
      ? $"W: {(int)KeyOverlayHeight}"
      : $"H: {(int)KeyOverlayHeight}";

    public double KeyOverlayBarSpeed
    {
      get => _presetConfig().KeyOverlayBarSpeed;
      set
      {
        _presetConfig().KeyOverlayBarSpeed = Math.Clamp(value, 50, 3000);
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

    public double KeyOverlayLaneWidth
    {
      get => _presetConfig().KeyOverlayLaneWidth;
      set
      {
        _presetConfig().KeyOverlayLaneWidth = Math.Clamp(value, 24, 160);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public void SetKeyOverlayPosition(double x, double y)
    {
      _presetConfig().KeyOverlayX = x;
      _presetConfig().KeyOverlayY = y;
      OnPropertyChanged(nameof(KeyOverlayX));
      OnPropertyChanged(nameof(KeyOverlayY));
      OnPropertyChanged(nameof(KeyOverlayPositionText));
      _save();
    }

    public void NotifyPresetApplied()
    {
      OnPropertyChanged(nameof(KeyOverlayEnabled));
      OnPropertyChanged(nameof(KeyOverlayHeight));
      OnPropertyChanged(nameof(KeyOverlayRotation));
      OnPropertyChanged(nameof(KeyOverlayRotationLabel));
      OnPropertyChanged(nameof(KeyOverlayX));
      OnPropertyChanged(nameof(KeyOverlayY));
      OnPropertyChanged(nameof(KeyOverlayPositionText));
      OnPropertyChanged(nameof(KeyOverlaySizeText));
      OnPropertyChanged(nameof(KeyOverlayBarSpeed));
      OnPropertyChanged(nameof(KeyOverlayBarRound));
      OnPropertyChanged(nameof(KeyOverlayLaneWidth));
    }
  }
}
