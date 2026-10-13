namespace OsuMate.Models
{
  public class ModePresetConfig
  {
    private volatile bool _autoSwitchEnabled;

    public bool AutoSwitchEnabled
    {
      get => _autoSwitchEnabled;
      set => _autoSwitchEnabled = value;
    }

    public Dictionary<int, string> PresetIdsByMode { get; set; } = new();

    public void Assign(int mode, string? presetId)
    {
      if (!OsuMode.IsValid(mode))
        return;

      if (string.IsNullOrEmpty(presetId))
        PresetIdsByMode.Remove(mode);
      else
        PresetIdsByMode[mode] = presetId;
    }

    public void RemovePreset(string presetId)
    {
      foreach (var (mode, assignedId) in PresetIdsByMode.ToArray())
      {
        if (assignedId == presetId)
          PresetIdsByMode.Remove(mode);
      }
    }

    public void Normalize(IReadOnlySet<string> validPresetIds)
    {
      PresetIdsByMode ??= new();

      foreach (var (mode, presetId) in PresetIdsByMode.ToArray())
      {
        if (!OsuMode.IsValid(mode) || string.IsNullOrEmpty(presetId) || !validPresetIds.Contains(presetId))
          PresetIdsByMode.Remove(mode);
      }
    }
  }
}
