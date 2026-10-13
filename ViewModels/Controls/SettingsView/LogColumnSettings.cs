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

    private readonly GlobalConfig _config;
    private readonly PriorityListSettings<LogColumnItem> _items;

    public ObservableCollection<LogColumnItem> LogColumnItems => _items.Items;

    public LogColumnSettings(GlobalConfig config, Action save)
    {
      _config = config;
      _items = new PriorityListSettings<LogColumnItem>(
        LogColumnDefinitions,
        "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15/16",
        true,
        (id, label, enabled) => new LogColumnItem { Id = id, Label = label, IsEnabled = enabled },
        save
      );
      _items.Load(_config.LogColumnPriority);
    }

    public void MoveLogColumnItem(int fromIndex, int toIndex) => _items.Move(fromIndex, toIndex);

    public string ToLogColumnPriorityString() => _items.ToPriorityString();
  }
}
