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
      set => SetField(ref _label, value);
    }

    public string Value
    {
      get => _value;
      set => SetField(ref _value, value);
    }
  }
}
