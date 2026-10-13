using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OsuMate.Models
{
  public class BulkObservableCollection<T> : ObservableCollection<T>
  {
    public void ReplaceAll(IEnumerable<T> items)
    {
      CheckReentrancy();
      var snapshot = items.ToList();
      Items.Clear();
      foreach (var item in snapshot)
        Items.Add(item);

      OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
      OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
  }
}
