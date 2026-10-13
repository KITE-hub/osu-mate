using System.Windows.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class SettingsViewModel : ObservableBase
  {
    public OverlaySettingsViewModel Overlay { get; }

    public URBarSettingsViewModel URBar { get; }
    public KeyOverlaySettingsViewModel KeyOverlay { get; }

    public PositionSettingsViewModel Position { get; }

    public LogColumnSettings LogColumnSettings { get; }

    public ObservableCollection<string> TargetPlayerNames { get; } = [];

    public ObservableCollection<Preset> Presets => _presetManager.Presets;

    private static readonly Lazy<IReadOnlyList<string>> LazyAvailableFonts = new(() =>
      OsuMate
        .Utils.AppFonts.EmbeddedFontNames.Append(OsuMate.Utils.AppFonts.FontListSeparator)
        .Concat(
          Fonts
            .SystemFontFamilies.Select(f => f.Source)
            .Where(s => !OsuMate.Utils.AppFonts.EmbeddedFontNames.Contains(s))
            .OrderBy(s => s)
        )
        .ToList()
    );

    public IReadOnlyList<string> AvailableFonts => LazyAvailableFonts.Value;

    public IReadOnlyList<UpdateIntervalOption> UpdateIntervalOptions { get; } =
      new List<UpdateIntervalOption>
      {
        new("15 fps", 66),
        new("30 fps", 33),
        new("60 fps", 16),
      };

    private readonly RootConfig _root;
    private readonly GlobalConfig _globalConfig;
    private readonly OsuMemoryService _memory;
    private readonly PresetManager _presetManager;

    private PresetConfig PresetConfig => _presetManager.ActiveConfig;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;

    public SettingsViewModel(
      OsuMemoryService memory,
      ConfigStore config,
      System.Windows.Threading.Dispatcher uiDispatcher
    )
    {
      _uiDispatcher = uiDispatcher;
      _memory = memory;
      _root = config.Root;
      _globalConfig = _root.Global;
      _globalConfig.DataUpdateIntervalMs = SnapToNearestUpdateInterval(_globalConfig.DataUpdateIntervalMs);

      _memory.SetManualOsuDirectory(_globalConfig.OsuExeDirectory);

      _memory.OnOsuDirectoryLoaded += dir =>
      {
        _uiDispatcher.BeginInvoke(() => OnPropertyChanged(nameof(OsuExeDirectoryText)));
      };

      _presetManager = new PresetManager(config);

      _saveTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _uiDispatcher
      )
      {
        Interval = TimeSpan.FromMilliseconds(500),
      };
      _saveTimer.Tick += (_, _) => RunDebouncedSave();

      Overlay = new OverlaySettingsViewModel(() => PresetConfig, Save, DebouncedSave);

      URBar = new URBarSettingsViewModel(() => PresetConfig, Save, DebouncedSave);

      KeyOverlay = new KeyOverlaySettingsViewModel(() => PresetConfig, Save, DebouncedSave);

      Position = new PositionSettingsViewModel(() => PresetConfig, Save);

      _presetManager.ActivePresetChanged += ApplyPresetConfig;

      foreach (var name in _globalConfig.TargetPlayerNames)
        TargetPlayerNames.Add(name);
      TargetPlayerNames.CollectionChanged += (_, _) =>
        _globalConfig.TargetPlayerNames = [.. TargetPlayerNames];

      LogColumnSettings = new LogColumnSettings(_globalConfig, Save);
    }

    public Preset? SelectedPreset
    {
      get => _presetManager.SelectedPreset;
      set => _presetManager.SelectedPreset = value;
    }

    private ICommand? _newPresetCommand;
    private ICommand? _duplicatePresetCommand;
    private ICommand? _renamePresetCommand;
    private ICommand? _removeTargetPlayerNameCommand;

    public ICommand NewPresetCommand => _newPresetCommand ??= new RelayCommand(_ => CreatePreset("New Preset"));

    public ICommand DuplicatePresetCommand =>
      _duplicatePresetCommand ??= new RelayCommand(_ =>
      {
        var current = SelectedPreset;
        if (current != null)
          DuplicatePreset(current.Id, current.Name + " - Copy");
      });

    public ICommand RenamePresetCommand =>
      _renamePresetCommand ??= new RelayCommand(parameter =>
      {
        var current = SelectedPreset;
        if (current != null && parameter is string name)
          RenamePreset(current.Id, name);
      });

    public ICommand RemoveTargetPlayerNameCommand =>
      _removeTargetPlayerNameCommand ??= new RelayCommand(parameter =>
      {
        if (parameter is string name)
          RemoveTargetPlayerName(name);
      });

    public Preset CreatePreset(string name) => _presetManager.CreatePreset(name);

    public Preset DuplicatePreset(string sourceId, string newName) =>
      _presetManager.DuplicatePreset(sourceId, newName);

    public void RenamePreset(string id, string newName) => _presetManager.RenamePreset(id, newName);

    public bool DeletePreset(string id) => _presetManager.DeletePreset(id);

    private void ApplyPresetConfig(PresetConfig config)
    {
      Overlay.NotifyPresetApplied();
      URBar.NotifyPresetApplied();
      KeyOverlay.NotifyPresetApplied();
      Position.NotifyPresetApplied();
      OnPropertyChanged(nameof(SelectedPreset));

      Overlay.RequestApplyOverlayPosition();
      URBar.RequestApplyURBarPosition();
      URBar.RequestApplyURBarSize();
      KeyOverlay.RequestApplyKeyOverlayPosition();
    }

    public void AddTargetPlayerName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return;
      name = name.Trim();
      if (TargetPlayerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        return;

      TargetPlayerNames.Add(name);
      Save();
    }

    public void RemoveTargetPlayerName(string name)
    {
      var existing = TargetPlayerNames.FirstOrDefault(n =>
        string.Equals(n, name, StringComparison.OrdinalIgnoreCase)
      );
      if (existing == null)
        return;

      TargetPlayerNames.Remove(existing);
      Save();
    }

    public string FontFamily
    {
      get => _globalConfig.FontFamily;
      set
      {
        if (value == OsuMate.Utils.AppFonts.FontListSeparator)
          return;
        _globalConfig.FontFamily = value;
        OnPropertyChanged();
        Save();
      }
    }
    public bool IsDarkMode
    {
      get => _globalConfig.IsDarkMode;
      set
      {
        _globalConfig.IsDarkMode = value;
        OnPropertyChanged();
        Save();
      }
    }

    public double ThemeHue
    {
      get => _globalConfig.ThemeHue;
      set
      {
        double snapped = Math.Clamp(Math.Round(value / 2.0) * 2.0, 0, 358);
        if (Math.Abs(_globalConfig.ThemeHue - snapped) < 0.01)
          return;
        _globalConfig.ThemeHue = snapped;
        OnPropertyChanged();
        DebouncedSave();
      }
    }

    public int DataUpdateIntervalMs
    {
      get => _globalConfig.DataUpdateIntervalMs;
      set
      {
        int snapped = SnapToNearestUpdateInterval(value);
        if (_globalConfig.DataUpdateIntervalMs == snapped)
          return;
        _globalConfig.DataUpdateIntervalMs = snapped;
        OnPropertyChanged();
        Save();
      }
    }

    private int SnapToNearestUpdateInterval(int value)
    {
      var nearest = UpdateIntervalOptions[0];
      var nearestDiff = Math.Abs(value - nearest.IntervalMs);
      foreach (var option in UpdateIntervalOptions)
      {
        var diff = Math.Abs(value - option.IntervalMs);
        if (diff < nearestDiff)
        {
          nearest = option;
          nearestDiff = diff;
        }
      }
      return nearest.IntervalMs;
    }

    public string OsuExeDirectory
    {
      get => _globalConfig.OsuExeDirectory;
      set
      {
        var normalized = value?.Trim() ?? "";
        if (_globalConfig.OsuExeDirectory == normalized)
          return;
        _globalConfig.OsuExeDirectory = normalized;

        _memory.SetManualOsuDirectory(normalized);
        OnPropertyChanged();
        OnPropertyChanged(nameof(OsuExeDirectoryText));
        Save();
      }
    }

    public string OsuExeDirectoryText
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(OsuExeDirectory))
          return Path.Combine(OsuExeDirectory, "osu!.exe");

        if (!string.IsNullOrWhiteSpace(_memory.OsuDirectory))
          return Path.Combine(_memory.OsuDirectory, "osu!.exe");

        return "(Auto-detecting...)";
      }
    }

    public bool AutoLaunchOsuEnabled
    {
      get => _globalConfig.AutoLaunchOsuEnabled;
      set
      {
        if (_globalConfig.AutoLaunchOsuEnabled == value)
          return;
        _globalConfig.AutoLaunchOsuEnabled = value;
        OnPropertyChanged();
        Save();
      }
    }

    public string AutoLaunchOsuPath
    {
      get => _globalConfig.AutoLaunchOsuPath;
      set
      {
        var normalized = value?.Trim() ?? "";
        if (_globalConfig.AutoLaunchOsuPath == normalized)
          return;
        _globalConfig.AutoLaunchOsuPath = normalized;
        OnPropertyChanged();
        OnPropertyChanged(nameof(AutoLaunchOsuPathText));
        Save();
      }
    }

    public string AutoLaunchOsuPathText =>
      string.IsNullOrWhiteSpace(AutoLaunchOsuPath) ? "(Do not auto-launch)" : AutoLaunchOsuPath;

    public bool ShowAbortedPlays
    {
      get => _globalConfig.ShowAbortedPlays;
      set
      {
        if (_globalConfig.ShowAbortedPlays == value)
          return;
        _globalConfig.ShowAbortedPlays = value;
        OnPropertyChanged();
        Save();
      }
    }

    private void DebouncedSave()
    {
      _saveTimer.Stop();
      _saveTimer.Start();
    }

    private void RunDebouncedSave()
    {
      _saveTimer.Stop();
      try
      {
        Save();
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("SettingsViewModel.DebouncedSave failed: " + e, true);
      }
    }

    public void Save()
    {
      PresetConfig.InGameOverlayPriority = Overlay.ToPriorityString();
      _globalConfig.LogColumnPriority = LogColumnSettings.ToLogColumnPriorityString();
      _presetManager.Save();
    }
  }
}
