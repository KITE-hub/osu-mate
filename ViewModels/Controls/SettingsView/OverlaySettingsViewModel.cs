using System.Windows.Input;
using System.Collections.ObjectModel;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public sealed class OverlaySettingsViewModel : ObservableBase
  {
    private static readonly (int id, string label)[] ItemDefinitions =
    [
      (OverlayLineIds.StarRating, "SR"),
      (OverlayLineIds.SsPp, "SS pp"),
      (OverlayLineIds.LossPp, "Lossmode pp"),
      (OverlayLineIds.PredictedPp, "Predicted pp"),
      (OverlayLineIds.CurrentPp, "Current pp"),
      (OverlayLineIds.Accuracy, "Accuracy"),
      (OverlayLineIds.Hits, "Hits"),
      (OverlayLineIds.AverageOffset, "Avg offset"),
      (OverlayLineIds.UniversalOffsetHelp, "Universal offset help"),
      (OverlayLineIds.LocalOffsetHelp, "Local offset help"),
      (OverlayLineIds.UnstableRate, "UR"),
      (OverlayLineIds.Progress, "Progress %"),
      (OverlayLineIds.RemainingNotes, "Remaining notes"),
      (OverlayLineIds.Bpm, "BPM"),
      (OverlayLineIds.BestPp, "Best pp"),
    ];

    private readonly PriorityListSettings<OverlayItem> _items;

    public ObservableCollection<OverlayItem> Items => _items.Items;

    public event Action? OnSaveOverlayPositionRequested;
    public event Action? OnApplyOverlayPositionRequested;

    private ICommand? _savePositionCommand;
    private ICommand? _applyPositionCommand;

    public ICommand SavePositionCommand =>
      _savePositionCommand ??= new RelayCommand(_ => RequestSaveOverlayPosition());

    public ICommand ApplyPositionCommand =>
      _applyPositionCommand ??= new RelayCommand(_ => RequestApplyOverlayPosition());

    public void RequestSaveOverlayPosition() => OnSaveOverlayPositionRequested?.Invoke();

    public void RequestApplyOverlayPosition() => OnApplyOverlayPositionRequested?.Invoke();

    private readonly Func<PresetConfig> _presetConfig;
    private readonly Action _save;
    private readonly Action _debouncedSave;

    public OverlaySettingsViewModel(
      Func<PresetConfig> presetConfig,
      Action save,
      Action debouncedSave
    )
    {
      _presetConfig = presetConfig;
      _save = save;
      _debouncedSave = debouncedSave;
      _items = new PriorityListSettings<OverlayItem>(
        ItemDefinitions,
        "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15",
        false,
        (id, label, enabled) => new OverlayItem { Id = id, Label = label, IsEnabled = enabled },
        save
      );
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

    public void LoadItems() => _items.Load(_presetConfig().InGameOverlayPriority);

    public void MoveItem(int fromIndex, int toIndex) => _items.Move(fromIndex, toIndex);

    public void ToggleItem(OverlayItem item) => _items.Toggle(item);

    public string ToPriorityString() => _items.ToPriorityString();

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
