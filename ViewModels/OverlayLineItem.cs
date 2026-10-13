using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public class OverlayLineItem : ObservableBase
  {
    private string _label = "";
    private string _value = "";

    public int Id { get; init; }

    public string Label
    {
      get => _label;
      set
      {
        _label = value;
        OnPropertyChanged();
      }
    }

    public string Value
    {
      get => _value;
      set
      {
        _value = value;
        OnPropertyChanged();
      }
    }
  }
}
