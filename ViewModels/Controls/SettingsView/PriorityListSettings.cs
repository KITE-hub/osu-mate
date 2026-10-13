using System.Collections.ObjectModel;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public sealed class PriorityListSettings<TItem>(
    (int id, string label)[] definitions,
    string fallbackPriority,
    bool missingItemEnabled,
    Func<int, string, bool, TItem> create,
    Action save
  )
    where TItem : class, IPriorityItem
  {
    public ObservableCollection<TItem> Items { get; } = [];

    public void Load(string? priority)
    {
      List<TItem> resolved;
      try
      {
        resolved = PriorityListParser.Resolve(
          priority,
          fallbackPriority,
          definitions,
          missingItemEnabled,
          create
        );
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("PriorityListSettings.Load failed: " + e, true);
        resolved = [.. definitions.Select(d => create(d.id, d.label, true))];
      }

      Items.Clear();
      foreach (var item in resolved)
        Items.Add(item);
    }

    public void Move(int fromIndex, int toIndex)
    {
      if (fromIndex == toIndex)
        return;
      if (fromIndex < 0 || fromIndex >= Items.Count)
        return;
      if (toIndex < 0 || toIndex >= Items.Count)
        return;

      var item = Items[fromIndex];
      Items.RemoveAt(fromIndex);
      Items.Insert(toIndex, item);
      save();
    }

    public void Toggle(TItem item)
    {
      item.IsEnabled = !item.IsEnabled;
      save();
    }

    public string ToPriorityString()
    {
      return PriorityListParser.ToPriorityString(Items.Select(i => (i.Id, i.IsEnabled)));
    }
  }
}
