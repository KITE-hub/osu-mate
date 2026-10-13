using System.Collections.ObjectModel;
using System.Text.Json;
using OsuMate.Models;

namespace OsuMate.Services
{
  public class PresetManager
  {
    private readonly ConfigStore _config;
    private readonly RootConfig _root;

    public ObservableCollection<Preset> Presets { get; } = [];

    public string ActivePresetId { get; private set; }

    public PresetConfig ActiveConfig { get; private set; }

    public event Action<PresetConfig>? ActivePresetChanged;

    public PresetManager(ConfigStore config)
    {
      _config = config;
      _root = config.Root;

      var active =
        _root.Presets.FirstOrDefault(p => p.Id == _root.ActivePresetId) ?? _root.Presets[0];
      ActivePresetId = active.Id;
      ActiveConfig = active.Config;

      foreach (var preset in _root.Presets)
        Presets.Add(preset);
    }

    private void AddPreset(Preset preset)
    {
      _root.Presets.Add(preset);
      Presets.Add(preset);
    }

    private void RemovePreset(Preset preset)
    {
      _root.ModePresets.RemovePreset(preset.Id);
      _root.Presets.Remove(preset);
      Presets.Remove(preset);
    }

    public Preset? SelectedPreset
    {
      get => Presets.FirstOrDefault(p => p.Id == ActivePresetId);
      set
      {
        if (value == null || value.Id == ActivePresetId)
          return;
        SwitchToPreset(value.Id);
      }
    }

    public void SwitchToPreset(string id)
    {
      var preset = Presets.FirstOrDefault(p => p.Id == id);
      if (preset == null)
        return;

      ActivePresetId = id;
      _root.ActivePresetId = id;
      ActiveConfig = preset.Config;

      ActivePresetChanged?.Invoke(ActiveConfig);
      Save();
    }

    public Preset CreatePreset(string name)
    {
      var preset = new Preset
      {
        Name = string.IsNullOrWhiteSpace(name) ? "New Preset" : name.Trim(),
        Config = new PresetConfig(),
      };
      AddPreset(preset);
      SwitchToPreset(preset.Id);
      return preset;
    }

    public Preset DuplicatePreset(string sourceId, string newName)
    {
      var source = Presets.FirstOrDefault(p => p.Id == sourceId) 
        ?? throw new ArgumentException($"Preset not found: {sourceId}", nameof(sourceId));
      var preset = new Preset
      {
        Name = string.IsNullOrWhiteSpace(newName) ? source.Name + " - Copy" : newName.Trim(),
        Config = CloneConfig(source.Config),
      };
      AddPreset(preset);
      SwitchToPreset(preset.Id);
      return preset;
    }

    public void RenamePreset(string id, string newName)
    {
      if (string.IsNullOrWhiteSpace(newName))
        return;
      var preset = Presets.FirstOrDefault(p => p.Id == id);
      if (preset == null)
        return;

      preset.Name = newName.Trim();
      Save();
    }

    public bool DeletePreset(string id)
    {
      if (Presets.Count <= 1)
        return false;
      var preset = Presets.FirstOrDefault(p => p.Id == id);
      if (preset == null)
        return false;

      bool wasActive = ActivePresetId == id;
      RemovePreset(preset);

      if (wasActive)
        SwitchToPreset(Presets[0].Id);
      else
        Save();

      return true;
    }

    private static PresetConfig CloneConfig(PresetConfig source)
    {
      var json = JsonSerializer.Serialize(source);
      return JsonSerializer.Deserialize<PresetConfig>(json) ?? new PresetConfig();
    }

    public void Save()
    {
      _config.Save();
    }
  }
}
