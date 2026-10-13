using System.Collections.ObjectModel;
using System.Collections.Specialized;
using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class ModePresetSettingsViewModel : ObservableBase
  {
    private static readonly Preset NoneOption = new() { Id = string.Empty, Name = "(None)" };

    private static readonly (int Mode, string Label)[] ModeDefinitions =
    [
      (OsuMode.Osu, "osu!"),
      (OsuMode.Taiko, "Taiko"),
      (OsuMode.Catch, "Catch"),
      (OsuMode.Mania, "Mania"),
    ];

    private readonly ModePresetConfig _config;
    private readonly ObservableCollection<Preset> _presets;
    private readonly Action _save;
    private readonly ObservableCollection<Preset> _options = [];

    public ReadOnlyObservableCollection<Preset> PresetOptions { get; }

    public IReadOnlyList<ModePresetRowViewModel> Rows { get; }

    public ModePresetSettingsViewModel(
      ModePresetConfig config,
      ObservableCollection<Preset> presets,
      Action save
    )
    {
      _config = config;
      _presets = presets;
      _save = save;

      PresetOptions = new ReadOnlyObservableCollection<Preset>(_options);
      RebuildOptions();

      Rows = ModeDefinitions
        .Select(definition => new ModePresetRowViewModel(definition.Mode, definition.Label, this))
        .ToList();

      _presets.CollectionChanged += OnPresetsChanged;
    }

    public bool AutoSwitchEnabled
    {
      get => _config.AutoSwitchEnabled;
      set
      {
        if (_config.AutoSwitchEnabled == value)
          return;

        _config.AutoSwitchEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }

    internal Preset GetAssignedOption(int mode)
    {
      if (!_config.PresetIdsByMode.TryGetValue(mode, out var presetId))
        return NoneOption;

      return _options.FirstOrDefault(option => option.Id == presetId) ?? NoneOption;
    }

    internal void Assign(int mode, Preset option)
    {
      if (!_options.Contains(option) || GetAssignedOption(mode) == option)
        return;

      _config.Assign(mode, option.Id);
      _save();
    }

    private void OnPresetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      switch (e.Action)
      {
        case NotifyCollectionChangedAction.Add when e.NewItems != null:
          foreach (Preset preset in e.NewItems)
            _options.Add(preset);
          break;
        case NotifyCollectionChangedAction.Remove when e.OldItems != null:
          foreach (Preset preset in e.OldItems)
            _options.Remove(preset);
          break;
        default:
          RebuildOptions();
          break;
      }

      foreach (var row in Rows)
        row.Refresh();
    }

    private void RebuildOptions()
    {
      _options.Clear();
      _options.Add(NoneOption);
      foreach (var preset in _presets)
        _options.Add(preset);
    }
  }
}
