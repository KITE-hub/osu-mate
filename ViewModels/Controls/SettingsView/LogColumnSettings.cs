using System;
using System.Collections.ObjectModel;
using System.Linq;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class LogColumnSettings
  {
    private static readonly (int id, string label)[] LogColumnDefinitions =
    [
      (1, "Date/Time"),
      (2, "Artist"),
      (3, "Title"),
      (4, "Difficulty"),
      (5, "Creator"),
      (6, "BID"),
      (7, "BSID"),
      (8, "SR"),
      (9, "OD"),
      (10, "pp"),
      (11, "Mods"),
      (12, "Acc"),
      (13, "Hits"),
      (14, "Combo"),
      (15, "Player"),
      (16, "Status"),
    ];

    public ObservableCollection<LogColumnItem> LogColumnItems { get; } = [];

    private readonly GlobalConfig _config;
    private readonly Action _save;

    private readonly object _logColumnLock = new();

    public LogColumnSettings(GlobalConfig config, Action save)
    {
      _config = config;
      _save = save;
      LoadLogColumnItems();
    }

    public void MoveLogColumnItem(int fromIndex, int toIndex)
    {
      if (fromIndex == toIndex)
        return;
      if (fromIndex < 0 || fromIndex >= LogColumnItems.Count)
        return;
      if (toIndex < 0 || toIndex >= LogColumnItems.Count)
        return;
      lock (_logColumnLock)
      {
        var item = LogColumnItems[fromIndex];
        LogColumnItems.RemoveAt(fromIndex);
        LogColumnItems.Insert(toIndex, item);
      }
      _save();
    }

    public string ToLogColumnPriorityString()
    {
      lock (_logColumnLock)
        return string.Join(
          "/",
          LogColumnItems.Select(i => i.IsEnabled ? i.Id.ToString() : $"-{i.Id}")
        );
    }

    private void LoadLogColumnItems()
    {
      try
      {
        var priority = _config.LogColumnPriority ?? "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15/16";
        var tokens = priority.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var ordered = tokens
          .Select(t =>
          {
            bool enabled = !t.StartsWith('-');
            string raw = enabled ? t : t[1..];
            if (int.TryParse(raw, out int id))
              return (Id: id, Enabled: enabled);
            return (Id: -1, Enabled: false);
          })
          .Where(x => x.Id > 0)
          .ToList();

        var orderedIds = ordered.Select(x => x.Id).ToList();

        lock (_logColumnLock)
        {
          LogColumnItems.Clear();
          foreach (var col in ordered)
          {
            var def = LogColumnDefinitions.FirstOrDefault(d => d.id == col.Id);
            if (def == default)
              continue;
            LogColumnItems.Add(
              new LogColumnItem
              {
                Id = col.Id,
                Label = def.label,
                IsEnabled = col.Enabled,
              }
            );
          }

          foreach (var def in LogColumnDefinitions)
          {
            if (orderedIds.Contains(def.id))
              continue;
            LogColumnItems.Add(
              new LogColumnItem
              {
                Id = def.id,
                Label = def.label,
                IsEnabled = true,
              }
            );
          }
        }
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("LogColumnSettings.LoadLogColumnItems failed: " + e.Message, true);
        LoadDefaultLogColumnItems();
      }
    }

    private void LoadDefaultLogColumnItems()
    {
      lock (_logColumnLock)
      {
        LogColumnItems.Clear();
        foreach (var def in LogColumnDefinitions)
          LogColumnItems.Add(
            new LogColumnItem
            {
              Id = def.id,
              Label = def.label,
              IsEnabled = true,
            }
          );
      }
    }
  }
}
