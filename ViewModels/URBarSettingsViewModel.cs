using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class URBarSettingsViewModel : ObservableBase
  {
    public event Action? OnSaveURBarPositionRequested;
    public event Action? OnApplyURBarPositionRequested;
    public event Action? OnSaveURBarSizeRequested;
    public event Action? OnApplyURBarSizeRequested;

    public void RequestSaveURBarPosition() => OnSaveURBarPositionRequested?.Invoke();

    public void RequestApplyURBarPosition() => OnApplyURBarPositionRequested?.Invoke();

    public void RequestSaveURBarSize() => OnSaveURBarSizeRequested?.Invoke();

    public void RequestApplyURBarSize() => OnApplyURBarSizeRequested?.Invoke();

    private readonly Func<PresetConfig> _presetConfig;
    private readonly Action _save;
    private readonly Action _debouncedSave;

    public URBarSettingsViewModel(
      Func<PresetConfig> presetConfig,
      Action save,
      Action debouncedSave
    )
    {
      _presetConfig = presetConfig;
      _save = save;
      _debouncedSave = debouncedSave;
    }

    public bool URBarEnabled
    {
      get => _presetConfig().URBarEnabled;
      set
      {
        _presetConfig().URBarEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }

    public int URBarRotation
    {
      get => _presetConfig().URBarRotation;
      set
      {
        _presetConfig().URBarRotation = ((value % 360) + 360) % 360;
        OnPropertyChanged();
        OnPropertyChanged(nameof(URBarRotationLabel));
        _save();
      }
    }
    public string URBarRotationLabel => $"{_presetConfig().URBarRotation}°";

    public double URBarWidth
    {
      get => _presetConfig().URBarWidth;
      set
      {
        _presetConfig().URBarWidth = Math.Max(40, value);
        OnPropertyChanged();
        OnPropertyChanged(nameof(URBarSizeText));
        _debouncedSave();
      }
    }
    public double URBarHeight
    {
      get => _presetConfig().URBarHeight;
      set
      {
        _presetConfig().URBarHeight = Math.Max(20, value);
        OnPropertyChanged();
        OnPropertyChanged(nameof(URBarSizeText));
        _debouncedSave();
      }
    }

    public double URBarX
    {
      get => _presetConfig().URBarX;
      set
      {
        _presetConfig().URBarX = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(URBarPositionText));
        _save();
      }
    }
    public double URBarY
    {
      get => _presetConfig().URBarY;
      set
      {
        _presetConfig().URBarY = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(URBarPositionText));
        _save();
      }
    }

    public string URBarPositionText => $"X: {(int)URBarX}  Y: {(int)URBarY}";
    public string URBarSizeText => $"W: {(int)URBarWidth}  H: {(int)URBarHeight}";

    public void SetURBarPosition(double x, double y)
    {
      _presetConfig().URBarX = x;
      _presetConfig().URBarY = y;
      OnPropertyChanged(nameof(URBarX));
      OnPropertyChanged(nameof(URBarY));
      OnPropertyChanged(nameof(URBarPositionText));
      _save();
    }

    public double URBarAvgLineFollowStrength
    {
      get => _presetConfig().URBarAvgLineFollowStrength;
      set
      {
        _presetConfig().URBarAvgLineFollowStrength = Math.Clamp(value, 0, 1);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double URBarAvgLineAnimMs
    {
      get => _presetConfig().URBarAvgLineAnimMs;
      set
      {
        _presetConfig().URBarAvgLineAnimMs = Math.Max(0, value);
        OnPropertyChanged();
        _save();
      }
    }

    public double URBarLabelOpacity
    {
      get => _presetConfig().URBarLabelOpacity;
      set
      {
        _presetConfig().URBarLabelOpacity = Math.Clamp(value, 0, 1);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double URBarSegmentOpacity
    {
      get => _presetConfig().URBarSegmentOpacity;
      set
      {
        _presetConfig().URBarSegmentOpacity = Math.Clamp(value, 0, 1);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double URBarMarkerOpacity
    {
      get => _presetConfig().URBarMarkerOpacity;
      set
      {
        _presetConfig().URBarMarkerOpacity = Math.Clamp(value, 0, 1);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public double URBarHitErrorOpacity
    {
      get => _presetConfig().URBarHitErrorOpacity;
      set
      {
        _presetConfig().URBarHitErrorOpacity = Math.Clamp(value, 0, 1);
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public void NotifyPresetApplied()
    {
      OnPropertyChanged(nameof(URBarEnabled));
      OnPropertyChanged(nameof(URBarRotation));
      OnPropertyChanged(nameof(URBarRotationLabel));
      OnPropertyChanged(nameof(URBarWidth));
      OnPropertyChanged(nameof(URBarHeight));
      OnPropertyChanged(nameof(URBarX));
      OnPropertyChanged(nameof(URBarY));
      OnPropertyChanged(nameof(URBarPositionText));
      OnPropertyChanged(nameof(URBarSizeText));
      OnPropertyChanged(nameof(URBarAvgLineFollowStrength));
      OnPropertyChanged(nameof(URBarAvgLineAnimMs));
      OnPropertyChanged(nameof(URBarLabelOpacity));
      OnPropertyChanged(nameof(URBarSegmentOpacity));
      OnPropertyChanged(nameof(URBarMarkerOpacity));
      OnPropertyChanged(nameof(URBarHitErrorOpacity));
    }
  }
}
