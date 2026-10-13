namespace OsuMate.Models
{
  public class LogColumnItem : ObservableBase, IPriorityItem
  {
    public int Id { get; set; }
    public string Label { get; set; } = "";

    private bool _isEnabled;
    public bool IsEnabled
    {
      get => _isEnabled;
      set
      {
        if (_isEnabled == value)
          return;
        _isEnabled = value;
        OnPropertyChanged();
      }
    }
  }
}
