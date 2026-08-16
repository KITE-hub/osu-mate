namespace OsuMate.Models
{
  public class OverlayItem : ObservableBase
  {
    public int Id { get; set; }
    public string Label { get; set; } = "";

    private bool _isEnabled = true;
    public bool IsEnabled
    {
      get => _isEnabled;
      set
      {
        _isEnabled = value;
        OnPropertyChanged();
      }
    }
  }
}
