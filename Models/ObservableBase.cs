using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OsuMate.Models
{
  public abstract class ObservableBase : INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
      if (EqualityComparer<T>.Default.Equals(field, value))
        return false;
      field = value;
      OnPropertyChanged(name);
      return true;
    }

    protected static bool TryMarkRendered(ref DateTime lastRenderedAt, int minIntervalMs)
    {
      DateTime now = DateTime.UtcNow;
      if ((now - lastRenderedAt).TotalMilliseconds < minIntervalMs)
        return false;
      lastRenderedAt = now;
      return true;
    }
  }
}
