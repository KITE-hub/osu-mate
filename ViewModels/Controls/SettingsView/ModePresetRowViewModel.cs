using System.Collections.ObjectModel;
using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class ModePresetRowViewModel : ObservableBase
  {
    private readonly int _mode;
    private readonly ModePresetSettingsViewModel _owner;

    public string Label { get; }

    public ReadOnlyObservableCollection<Preset> PresetOptions => _owner.PresetOptions;

    public ModePresetRowViewModel(int mode, string label, ModePresetSettingsViewModel owner)
    {
      _mode = mode;
      Label = label;
      _owner = owner;
    }

    public Preset? SelectedPreset
    {
      get => _owner.GetAssignedOption(_mode);
      set
      {
        if (value == null)
          return;

        _owner.Assign(_mode, value);
        OnPropertyChanged();
      }
    }

    public void Refresh() => OnPropertyChanged(nameof(SelectedPreset));
  }
}
