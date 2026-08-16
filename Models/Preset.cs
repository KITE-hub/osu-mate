using System;

namespace OsuMate.Models
{
  public class Preset : ObservableBase
  {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = "Default";
    public string Name
    {
      get => _name;
      set
      {
        if (_name == value)
          return;
        _name = value;
        OnPropertyChanged();
      }
    }

    public PresetConfig Config { get; set; } = new();
  }
}
