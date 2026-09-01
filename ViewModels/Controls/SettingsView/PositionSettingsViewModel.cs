using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class PositionSettingsViewModel : ObservableBase
  {
    private readonly Func<PresetConfig> _presetConfig;
    private readonly Action _save;

    public PositionSettingsViewModel(Func<PresetConfig> presetConfig, Action save)
    {
      _presetConfig = presetConfig;
      _save = save;
    }

    public bool AppPositionEnabled
    {
      get => _presetConfig().AppPositionEnabled;
      set
      {
        _presetConfig().AppPositionEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }
    public double AppX
    {
      get => _presetConfig().AppX;
      set
      {
        _presetConfig().AppX = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(AppPositionText));
        _save();
      }
    }
    public double AppY
    {
      get => _presetConfig().AppY;
      set
      {
        _presetConfig().AppY = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(AppPositionText));
        _save();
      }
    }
    public string AppPositionText => $"X: {(int)AppX}  Y: {(int)AppY}";

    public void SetAppPosition(double x, double y)
    {
      AppX = x;
      AppY = y;
    }

    public bool OsuPositionEnabled
    {
      get => _presetConfig().OsuPositionEnabled;
      set
      {
        _presetConfig().OsuPositionEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }
    public double OsuX
    {
      get => _presetConfig().OsuX;
      set
      {
        _presetConfig().OsuX = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(OsuPositionText));
        _save();
      }
    }
    public double OsuY
    {
      get => _presetConfig().OsuY;
      set
      {
        _presetConfig().OsuY = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(OsuPositionText));
        _save();
      }
    }
    public string OsuPositionText => $"X: {(int)OsuX}  Y: {(int)OsuY}";

    public void SetOsuPosition(double x, double y)
    {
      OsuX = x;
      OsuY = y;
    }

    public bool StartupPositionEnabled
    {
      get => _presetConfig().AppPositionEnabled;
      set
      {
        _presetConfig().AppPositionEnabled = value;
        _presetConfig().OsuPositionEnabled = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(AppPositionEnabled));
        OnPropertyChanged(nameof(OsuPositionEnabled));
        _save();
      }
    }

    public void NotifyPresetApplied()
    {
      OnPropertyChanged(nameof(AppPositionEnabled));
      OnPropertyChanged(nameof(AppX));
      OnPropertyChanged(nameof(AppY));
      OnPropertyChanged(nameof(AppPositionText));
      OnPropertyChanged(nameof(OsuPositionEnabled));
      OnPropertyChanged(nameof(OsuX));
      OnPropertyChanged(nameof(OsuY));
      OnPropertyChanged(nameof(OsuPositionText));
      OnPropertyChanged(nameof(StartupPositionEnabled));
    }
  }
}
