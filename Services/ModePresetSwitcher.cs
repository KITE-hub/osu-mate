using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services
{
  public sealed class ModePresetSwitcher : IDisposable
  {
    private const int NoMode = -1;
    private const long StableDurationMs = 300;

    private readonly OsuMemoryService _memory;
    private readonly PresetManager _presetManager;
    private readonly ModePresetConfig _config;
    private readonly Dispatcher _uiDispatcher;

    private int _appliedMode = NoMode;
    private int _candidateMode = NoMode;
    private long _candidateSinceMs;
    private volatile bool _resetRequested;
    private bool _started;

    public ModePresetSwitcher(
      OsuMemoryService memory,
      PresetManager presetManager,
      ConfigStore config,
      Dispatcher uiDispatcher
    )
    {
      _memory = memory;
      _presetManager = presetManager;
      _config = config.Root.ModePresets;
      _uiDispatcher = uiDispatcher;
    }

    public void Start()
    {
      if (_started)
        return;

      _started = true;
      _memory.OnMemoryRead += HandleMemoryRead;
      _memory.OnOsuWindowFound += HandleOsuWindowFound;
    }

    public void Dispose()
    {
      if (!_started)
        return;

      _started = false;
      _memory.OnMemoryRead -= HandleMemoryRead;
      _memory.OnOsuWindowFound -= HandleOsuWindowFound;
    }

    private void HandleOsuWindowFound(IntPtr handle) => _resetRequested = true;

    private void HandleMemoryRead()
    {
      if (_resetRequested)
      {
        _resetRequested = false;
        ResetObservation();
      }

      if (!_config.AutoSwitchEnabled)
      {
        ResetObservation();
        return;
      }

      int mode = _memory.CurrentOsuGamemode;
      if (!OsuMode.IsValid(mode))
        return;

      long now = Environment.TickCount64;
      if (mode != _candidateMode)
      {
        _candidateMode = mode;
        _candidateSinceMs = now;
      }

      if (mode == _appliedMode || now - _candidateSinceMs < StableDurationMs || _memory.IsPlaying)
        return;

      _appliedMode = mode;
      _uiDispatcher.BeginInvoke(() => ApplyAssignedPreset(mode));
    }

    private void ResetObservation()
    {
      _appliedMode = NoMode;
      _candidateMode = NoMode;
    }

    private void ApplyAssignedPreset(int mode)
    {
      try
      {
        if (!_config.AutoSwitchEnabled)
          return;

        if (!_config.PresetIdsByMode.TryGetValue(mode, out var presetId))
          return;

        if (presetId == _presetManager.ActivePresetId)
          return;

        _presetManager.SwitchToPreset(presetId);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("ModePresetSwitcher.ApplyAssignedPreset failed: " + e, true);
      }
    }
  }
}
