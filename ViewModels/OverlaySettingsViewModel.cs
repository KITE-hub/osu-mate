using System.Collections.ObjectModel;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public sealed class OverlaySettingsViewModel : ObservableBase
  {
    private static readonly (int id, string label)[] ItemDefinitions =
    [
      (1, "SR"),
      (2, "SS pp"),
      (3, "Lossmode pp"),
      (4, "Predicted pp"),
      (5, "Current pp"),
      (6, "Accuracy"),
      (7, "Hits"),
      (8, "Avg offset"),
      (9, "Universal offset help"),
      (10, "Local offset help"),
      (11, "UR"),
      (12, "Progress %"),
      (13, "Remaining notes"),
      (14, "BPM"),
      (15, "Best pp"),
    ];

    public ObservableCollection<OverlayItem> Items { get; } = [];

    public event Action? OnSaveOverlayPositionRequested;
    public event Action? OnApplyOverlayPositionRequested;

    public void RequestSaveOverlayPosition() => OnSaveOverlayPositionRequested?.Invoke();

    public void RequestApplyOverlayPosition() => OnApplyOverlayPositionRequested?.Invoke();

    private readonly Func<PresetConfig> _presetConfig;
    private readonly Action _save;
    private readonly Action _debouncedSave;

    private readonly object _itemsLock = new();

    public OverlaySettingsViewModel(
      Func<PresetConfig> presetConfig,
      Action save,
      Action debouncedSave
    )
    {
      _presetConfig = presetConfig;
      _save = save;
      _debouncedSave = debouncedSave;
      LoadItems();
    }

    public double OverlayX
    {
      get => _presetConfig().OverlayX;
      set
      {
        _presetConfig().OverlayX = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(OverlayPositionText));
        _save();
      }
    }
    public double OverlayY
    {
      get => _presetConfig().OverlayY;
      set
      {
        _presetConfig().OverlayY = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(OverlayPositionText));
        _save();
      }
    }
    public string OverlayPositionText => $"X: {(int)OverlayX}  Y: {(int)OverlayY}";

    public void SetOverlayPosition(double x, double y)
    {
      _presetConfig().OverlayX = x;
      _presetConfig().OverlayY = y;
      OnPropertyChanged(nameof(OverlayX));
      OnPropertyChanged(nameof(OverlayY));
      OnPropertyChanged(nameof(OverlayPositionText));
      _save();
    }

    public bool OverlayEnabled
    {
      get => _presetConfig().OverlayEnabled;
      set
      {
        _presetConfig().OverlayEnabled = value;
        OnPropertyChanged();
        _save();
      }
    }

    public double OverlayFontSize
    {
      get => _presetConfig().OverlayFontSize;
      set
      {
        _presetConfig().OverlayFontSize = value;
        OnPropertyChanged();
        _debouncedSave();
      }
    }

    public bool IsShowValueFirst
    {
      get => _presetConfig().IsShowValueFirst;
      set
      {
        _presetConfig().IsShowValueFirst = value;
        OnPropertyChanged();
        _save();
      }
    }

    public void LoadItems()
    {
      try
      {
        var priority =
          _presetConfig().InGameOverlayPriority ?? "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15";
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

        lock (_itemsLock)
        {
          Items.Clear();
          foreach (var item in ordered)
          {
            var def = ItemDefinitions.FirstOrDefault(d => d.id == item.Id);
            if (def == default)
              continue;
            Items.Add(
              new OverlayItem
              {
                Id = item.Id,
                Label = def.label,
                IsEnabled = item.Enabled,
              }
            );
          }

          foreach (var def in ItemDefinitions)
          {
            if (orderedIds.Contains(def.id))
              continue;
            Items.Add(
              new OverlayItem
              {
                Id = def.id,
                Label = def.label,
                IsEnabled = false,
              }
            );
          }
        }
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("OverlaySettingsViewModel.LoadItems failed: " + e.Message, true);
        LoadDefaultItems();
      }
    }

    private void LoadDefaultItems()
    {
      lock (_itemsLock)
      {
        Items.Clear();
        foreach (var def in ItemDefinitions)
          Items.Add(
            new OverlayItem
            {
              Id = def.id,
              Label = def.label,
              IsEnabled = true,
            }
          );
      }
    }

    public void MoveItem(int fromIndex, int toIndex)
    {
      if (fromIndex == toIndex)
        return;
      if (fromIndex < 0 || fromIndex >= Items.Count)
        return;
      if (toIndex < 0 || toIndex >= Items.Count)
        return;
      lock (_itemsLock)
      {
        var item = Items[fromIndex];
        Items.RemoveAt(fromIndex);
        Items.Insert(toIndex, item);
      }
      _save();
    }

    public void ToggleItem(OverlayItem item)
    {
      lock (_itemsLock)
      {
        item.IsEnabled = !item.IsEnabled;
      }
      _save();
    }

    public string ToPriorityString()
    {
      lock (_itemsLock)
        return string.Join("/", Items.Select(i => i.IsEnabled ? i.Id.ToString() : $"-{i.Id}"));
    }

    public void NotifyPresetApplied()
    {
      OnPropertyChanged(nameof(OverlayX));
      OnPropertyChanged(nameof(OverlayY));
      OnPropertyChanged(nameof(OverlayPositionText));
      OnPropertyChanged(nameof(OverlayEnabled));
      OnPropertyChanged(nameof(OverlayFontSize));
      OnPropertyChanged(nameof(IsShowValueFirst));
      LoadItems();
    }
  }
}
