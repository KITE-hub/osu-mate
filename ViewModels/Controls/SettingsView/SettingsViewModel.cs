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

    public IReadOnlyList<string> AvailableFonts { get; } =
      OsuMate
        .Utils.AppFonts.EmbeddedFontNames.Append(OsuMate.Utils.AppFonts.FontListSeparator)
        .Concat(
          Fonts
            .SystemFontFamilies.Select(f => f.Source)
            .Where(s => !OsuMate.Utils.AppFonts.EmbeddedFontNames.Contains(s))
            .OrderBy(s => s)
        )
        .ToList();

    public IReadOnlyList<UpdateIntervalOption> UpdateIntervalOptions { get; } =
      new List<UpdateIntervalOption>
      {
        new("15 fps", 66),
        new("30 fps", 33),
        new("60 fps", 16),
      };

    public event Action? OnSaveOverlayPositionRequested
    {
      add => Overlay.OnSaveOverlayPositionRequested += value;
      remove => Overlay.OnSaveOverlayPositionRequested -= value;
    }
    public event Action? OnApplyOverlayPositionRequested
    {
      add => Overlay.OnApplyOverlayPositionRequested += value;
      remove => Overlay.OnApplyOverlayPositionRequested -= value;
    }

    public void RequestSaveOverlayPosition() => Overlay.RequestSaveOverlayPosition();

    public void RequestApplyOverlayPosition() => Overlay.RequestApplyOverlayPosition();

    public event Action? OnSaveURBarPositionRequested
    {
      add => URBar.OnSaveURBarPositionRequested += value;
      remove => URBar.OnSaveURBarPositionRequested -= value;
    }
    public event Action? OnApplyURBarPositionRequested
    {
      add => URBar.OnApplyURBarPositionRequested += value;
      remove => URBar.OnApplyURBarPositionRequested -= value;
    }
    public event Action? OnSaveURBarSizeRequested
    {
      add => URBar.OnSaveURBarSizeRequested += value;
      remove => URBar.OnSaveURBarSizeRequested -= value;
    }
    public event Action? OnApplyURBarSizeRequested
    {
      add => URBar.OnApplyURBarSizeRequested += value;
      remove => URBar.OnApplyURBarSizeRequested -= value;
    }

    public event Action? OnSaveKeyOverlayPositionRequested
    {
      add => KeyOverlay.OnSaveKeyOverlayPositionRequested += value;
      remove => KeyOverlay.OnSaveKeyOverlayPositionRequested -= value;
    }

    public event Action? OnApplyKeyOverlayPositionRequested
    {
      add => KeyOverlay.OnApplyKeyOverlayPositionRequested += value;
      remove => KeyOverlay.OnApplyKeyOverlayPositionRequested -= value;
    }

    public event Action? OnSaveKeyOverlayFlowLengthRequested
    {
      add => KeyOverlay.OnSaveKeyOverlayFlowLengthRequested += value;
      remove => KeyOverlay.OnSaveKeyOverlayFlowLengthRequested -= value;
    }

    public event Action? OnApplyKeyOverlayFlowLengthRequested
    {
      add => KeyOverlay.OnApplyKeyOverlayFlowLengthRequested += value;
      remove => KeyOverlay.OnApplyKeyOverlayFlowLengthRequested -= value;
    }

    public void RequestSaveURBarPosition() => URBar.RequestSaveURBarPosition();

    public void RequestApplyURBarPosition() => URBar.RequestApplyURBarPosition();

    public void RequestSaveURBarSize() => URBar.RequestSaveURBarSize();

    public void RequestApplyURBarSize() => URBar.RequestApplyURBarSize();

    public void RequestSaveKeyOverlayPosition() => KeyOverlay.RequestSaveKeyOverlayPosition();

    public void RequestApplyKeyOverlayPosition() => KeyOverlay.RequestApplyKeyOverlayPosition();

    public void RequestSaveKeyOverlayFlowLength() => KeyOverlay.RequestSaveKeyOverlayFlowLength();

    public void RequestApplyKeyOverlayFlowLength() => KeyOverlay.RequestApplyKeyOverlayFlowLength();

    private readonly RootConfig _root;
    private readonly GlobalConfig _globalConfig;
    private readonly OsuMemoryService _memory;
    private readonly PresetManager _presetManager;

    private PresetConfig _presetConfig => _presetManager.ActiveConfig;
    private System.Threading.Timer? _saveTimer;
    private System.Windows.Threading.Dispatcher? _uiDispatcher;

    public void AttachUiDispatcher(System.Windows.Threading.Dispatcher dispatcher)
    {
      _uiDispatcher = dispatcher;
    }

    public SettingsViewModel(OsuMemoryService memory)
    {
      _memory = memory;
      _root = ConfigUtils.LoadRootConfig();
      _globalConfig = _root.Global;
      _globalConfig.DataUpdateIntervalMs = SnapToNearestUpdateInterval(_globalConfig.DataUpdateIntervalMs);

      _memory.SetManualOsuDirectory(_globalConfig.OsuExeDirectory);

      _memory.OnOsuDirectoryLoaded += dir =>
      {
        _uiDispatcher?.BeginInvoke(() =>
        {
          OsuExeDirectory = dir;
        });
      };

      _presetManager = new PresetManager(_root);

      Overlay = new OverlaySettingsViewModel(() => _presetConfig, Save, DebouncedSave);
      Overlay.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

      URBar = new URBarSettingsViewModel(() => _presetConfig, Save, DebouncedSave);
      URBar.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

      KeyOverlay = new KeyOverlaySettingsViewModel(() => _presetConfig, Save, DebouncedSave);
      KeyOverlay.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

      Position = new PositionSettingsViewModel(() => _presetConfig, Save);
      Position.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

      _presetManager.ActivePresetChanged += ApplyPresetConfig;

      foreach (var name in _globalConfig.TargetPlayerNames)
        TargetPlayerNames.Add(name);

      LogColumnSettings = new LogColumnSettings(_globalConfig, Save);
    }

    public Preset? SelectedPreset
    {
      get => _presetManager.SelectedPreset;
      set => _presetManager.SelectedPreset = value;
    }

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

      RequestApplyOverlayPosition();
      RequestApplyURBarPosition();
      RequestApplyURBarSize();
      RequestApplyKeyOverlayPosition();
    }

    public void AddTargetPlayerName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return;
      name = name.Trim();
      if (TargetPlayerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        return;

      TargetPlayerNames.Add(name);
      _globalConfig.TargetPlayerNames = [.. TargetPlayerNames];
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
      _globalConfig.TargetPlayerNames = [.. TargetPlayerNames];
      Save();
    }

    public double OverlayX
    {
      get => Overlay.OverlayX;
      set => Overlay.OverlayX = value;
    }
    public double OverlayY
    {
      get => Overlay.OverlayY;
      set => Overlay.OverlayY = value;
    }
    public string OverlayPositionText => Overlay.OverlayPositionText;

    public void SetOverlayPosition(double x, double y) => Overlay.SetOverlayPosition(x, y);

    public bool OverlayEnabled
    {
      get => Overlay.OverlayEnabled;
      set => Overlay.OverlayEnabled = value;
    }
    public double OverlayFontSize
    {
      get => Overlay.OverlayFontSize;
      set => Overlay.OverlayFontSize = value;
    }
    public bool IsShowValueFirst
    {
      get => Overlay.IsShowValueFirst;
      set => Overlay.IsShowValueFirst = value;
    }

    public bool URBarEnabled
    {
      get => URBar.URBarEnabled;
      set => URBar.URBarEnabled = value;
    }
    public bool KeyOverlayEnabled
    {
      get => KeyOverlay.KeyOverlayEnabled;
      set => KeyOverlay.KeyOverlayEnabled = value;
    }

    public int KeyOverlayRotation
    {
      get => KeyOverlay.KeyOverlayRotation;
      set => KeyOverlay.KeyOverlayRotation = value;
    }

    public double KeyOverlayLaneWidth
    {
      get => KeyOverlay.KeyOverlayLaneWidth;
      set => KeyOverlay.KeyOverlayLaneWidth = value;
    }

    public double KeyOverlayHeight
    {
      get => KeyOverlay.KeyOverlayHeight;
      set => KeyOverlay.KeyOverlayHeight = value;
    }

    public string KeyOverlayRotationLabel => KeyOverlay.KeyOverlayRotationLabel;
    public string KeyOverlaySizeText => KeyOverlay.KeyOverlaySizeText;
    public double KeyOverlayX
    {
      get => KeyOverlay.KeyOverlayX;
      set => KeyOverlay.KeyOverlayX = value;
    }
    public double KeyOverlayY
    {
      get => KeyOverlay.KeyOverlayY;
      set => KeyOverlay.KeyOverlayY = value;
    }
    public string KeyOverlayPositionText => KeyOverlay.KeyOverlayPositionText;
    public void SetKeyOverlayPosition(double x, double y) => KeyOverlay.SetKeyOverlayPosition(x, y);
    public double KeyOverlayDurationMs
    {
      get => KeyOverlay.KeyOverlayDurationMs;
      set => KeyOverlay.KeyOverlayDurationMs = value;
    }
    public double KeyOverlayBarRound
    {
      get => KeyOverlay.KeyOverlayBarRound;
      set => KeyOverlay.KeyOverlayBarRound = value;
    }
    public bool KeyOverlayShowBeatmapBars
    {
      get => KeyOverlay.KeyOverlayShowBeatmapBars;
      set => KeyOverlay.KeyOverlayShowBeatmapBars = value;
    }
    public int KeyOverlayBeatmapLanePosition
    {
      get => KeyOverlay.KeyOverlayBeatmapLanePosition;
      set => KeyOverlay.KeyOverlayBeatmapLanePosition = value;
    }
    public bool KeyOverlayBeatmapLaneAtEnd
    {
      get => KeyOverlay.KeyOverlayBeatmapLaneAtEnd;
      set => KeyOverlay.KeyOverlayBeatmapLaneAtEnd = value;
    }
    public string KeyOverlayBeatmapLanePositionLabel => KeyOverlay.KeyOverlayBeatmapLanePositionLabel;
    public double KeyOverlayInputBarOpacity
    {
      get => KeyOverlay.KeyOverlayInputBarOpacity;
      set => KeyOverlay.KeyOverlayInputBarOpacity = value;
    }
    public double KeyOverlayBeatmapBarOpacity
    {
      get => KeyOverlay.KeyOverlayBeatmapBarOpacity;
      set => KeyOverlay.KeyOverlayBeatmapBarOpacity = value;
    }
    public double KeyOverlayBeatmapTapLengthMs
    {
      get => KeyOverlay.KeyOverlayBeatmapTapLengthMs;
      set => KeyOverlay.KeyOverlayBeatmapTapLengthMs = value;
    }

    public int URBarRotation
    {
      get => URBar.URBarRotation;
      set => URBar.URBarRotation = value;
    }
    public string URBarRotationLabel => URBar.URBarRotationLabel;
    public double URBarWidth
    {
      get => URBar.URBarWidth;
      set => URBar.URBarWidth = value;
    }
    public double URBarHeight
    {
      get => URBar.URBarHeight;
      set => URBar.URBarHeight = value;
    }
    public double URBarX
    {
      get => URBar.URBarX;
      set => URBar.URBarX = value;
    }
    public double URBarY
    {
      get => URBar.URBarY;
      set => URBar.URBarY = value;
    }
    public string URBarPositionText => URBar.URBarPositionText;

    public void SetURBarPosition(double x, double y) => URBar.SetURBarPosition(x, y);

    public string URBarSizeText => URBar.URBarSizeText;
    public double URBarAvgLineFollowStrength
    {
      get => URBar.URBarAvgLineFollowStrength;
      set => URBar.URBarAvgLineFollowStrength = value;
    }
    public double URBarAvgLineAnimMs
    {
      get => URBar.URBarAvgLineAnimMs;
      set => URBar.URBarAvgLineAnimMs = value;
    }
    public double URBarLabelOpacity
    {
      get => URBar.URBarLabelOpacity;
      set => URBar.URBarLabelOpacity = value;
    }
    public double URBarSegmentOpacity
    {
      get => URBar.URBarSegmentOpacity;
      set => URBar.URBarSegmentOpacity = value;
    }
    public double URBarMarkerOpacity
    {
      get => URBar.URBarMarkerOpacity;
      set => URBar.URBarMarkerOpacity = value;
    }
    public double URBarHitErrorOpacity
    {
      get => URBar.URBarHitErrorOpacity;
      set => URBar.URBarHitErrorOpacity = value;
    }

    public bool AppPositionEnabled
    {
      get => Position.AppPositionEnabled;
      set => Position.AppPositionEnabled = value;
    }
    public double AppX
    {
      get => Position.AppX;
      set => Position.AppX = value;
    }
    public double AppY
    {
      get => Position.AppY;
      set => Position.AppY = value;
    }
    public string AppPositionText => Position.AppPositionText;

    public void SetAppPosition(double x, double y) => Position.SetAppPosition(x, y);

    public bool OsuPositionEnabled
    {
      get => Position.OsuPositionEnabled;
      set => Position.OsuPositionEnabled = value;
    }
    public double OsuX
    {
      get => Position.OsuX;
      set => Position.OsuX = value;
    }
    public double OsuY
    {
      get => Position.OsuY;
      set => Position.OsuY = value;
    }
    public string OsuPositionText => Position.OsuPositionText;

    public void SetOsuPosition(double x, double y) => Position.SetOsuPosition(x, y);

    public bool StartupPositionEnabled
    {
      get => Position.StartupPositionEnabled;
      set => Position.StartupPositionEnabled = value;
    }

    public ObservableCollection<OverlayItem> Items => Overlay.Items;

    public void MoveItem(int fromIndex, int toIndex) => Overlay.MoveItem(fromIndex, toIndex);

    public void ToggleItem(OverlayItem item) => Overlay.ToggleItem(item);

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
      _saveTimer?.Dispose();
      _saveTimer = new System.Threading.Timer(
        _ =>
          _uiDispatcher?.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
              try
              {
                Save();
              }
              catch (Exception e)
              {
                LogUtils.DebugLogger("SettingsViewModel.DebouncedSave failed: " + e.Message, true);
              }
            })
          ),
        null,
        500,
        Timeout.Infinite
      );
    }

    public void Save()
    {
      _presetConfig.InGameOverlayPriority = Overlay.ToPriorityString();
      _globalConfig.LogColumnPriority = LogColumnSettings.ToLogColumnPriorityString();
      _presetManager.Save();
    }
  }
}
