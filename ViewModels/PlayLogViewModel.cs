using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.ViewModels
{
  public class PlayLogViewModel : ObservableBase, IDisposable
  {
    private readonly PlayLogService _service;
    private readonly SettingsViewModel _settings;
    private readonly Dispatcher _dispatcher;
    private readonly ContributionGraphViewModel _contributionGraphViewModel;
    private readonly ContributionChartViewModel _contributionChartViewModel;
    private readonly PlayStatsChartViewModel _playStatsChartViewModel;

    private List<PlayLogEntry> _lastFilteredEntries = [];

    public ContributionGraphViewModel ContributionGraphVM => _contributionGraphViewModel;

    public ContributionChartViewModel ContributionChartVM => _contributionChartViewModel;

    public PlayStatsChartViewModel PlayStatsChartVM => _playStatsChartViewModel;

    public ObservableCollection<PlayLogEntry> Entries => _service.Entries;

    private readonly ICollectionView _filteredEntries;
    private readonly HashSet<LogColumnItem> _subscribedLogColumnItems = [];
    public ICollectionView FilteredEntries => _filteredEntries;

    private LogModeCategory _selectedModeCategory = LogModeCategory.Standard;
    public LogModeCategory SelectedModeCategory
    {
      get => _selectedModeCategory;
      private set
      {
        if (_selectedModeCategory == value)
          return;
        _selectedModeCategory = value;
        _filteredEntries.Refresh();
        OnPropertyChanged();
        NotifyFilteredEntriesChanged();
      }
    }

    public int FilteredEntryCount => _filteredEntries.Cast<object>().Count();

    public IReadOnlyList<LogColumnItem> AllColumns =>
      _settings.LogColumnSettings.LogColumnItems.ToList();

    public IReadOnlyList<LogColumnItem> ActiveColumns =>
      _settings.LogColumnSettings.LogColumnItems.Where(c => c.IsEnabled).ToList();

    private bool _isLoading = false;
    public bool IsLoading
    {
      get => _isLoading;
      set
      {
        _isLoading = value;
        RaiseOnUiThread(nameof(IsLoading));
      }
    }

    private string _statusText = "Loading logs...";
    public string StatusText
    {
      get => _statusText;
      set
      {
        _statusText = value;
        RaiseOnUiThread(nameof(StatusText));
      }
    }

    private void RaiseOnUiThread(string propertyName)
    {
      if (_dispatcher.CheckAccess())
        OnPropertyChanged(propertyName);
      else
        _dispatcher.BeginInvoke(
          System.Windows.Threading.DispatcherPriority.DataBind,
          new Action(() => OnPropertyChanged(propertyName))
        );
    }

    public PlayLogViewModel(
      PlayLogService service,
      SettingsViewModel settings,
      ContributionGraphViewModel contributionGraphViewModel,
      ContributionChartViewModel contributionChartViewModel,
      PlayStatsChartViewModel playStatsChartViewModel
    )
    {
      _service = service;
      _settings = settings;
      _dispatcher = Dispatcher.CurrentDispatcher;
      _service.AttachUiDispatcher(_dispatcher);
      _contributionGraphViewModel = contributionGraphViewModel;
      _contributionChartViewModel = contributionChartViewModel;
      _playStatsChartViewModel = playStatsChartViewModel;
      _filteredEntries = CollectionViewSource.GetDefaultView(Entries);
      _filteredEntries.Filter = item =>
        item is PlayLogEntry entry
        && entry.ModeCategory == SelectedModeCategory
        && PassesNonModeFilters(entry);
      Entries.CollectionChanged += (_, _) => NotifyFilteredEntriesChanged();

      _settings.LogColumnSettings.LogColumnItems.CollectionChanged += OnLogColumnItemsChanged;
      foreach (var item in _settings.LogColumnSettings.LogColumnItems)
        SubscribeLogColumnItem(item);

      _settings.TargetPlayerNames.CollectionChanged += (_, _) =>
      {
        _filteredEntries.Refresh();
        NotifyFilteredEntriesChanged();
      };

      _settings.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName != nameof(SettingsViewModel.ShowAbortedPlays))
          return;
        _filteredEntries.Refresh();
        NotifyFilteredEntriesChanged();
      };

      _contributionGraphViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName != nameof(ContributionGraphViewModel.CurrentMonth))
          return;
        _contributionChartViewModel.Recalculate(
          _contributionGraphViewModel.DailyHits,
          _contributionGraphViewModel.CurrentMonth
        );
        _playStatsChartViewModel.Recalculate(
          _lastFilteredEntries,
          _contributionGraphViewModel.DailyStats,
          _contributionGraphViewModel.CurrentMonth
        );
      };
    }

    public void SelectModeCategory(LogModeCategory category) => SelectedModeCategory = category;

    private bool PassesNonModeFilters(PlayLogEntry entry) =>
      TargetPlayerFilter.Matches(entry.PlayerName, _settings.TargetPlayerNames)
      && (_settings.ShowAbortedPlays || entry.IsCompleted);

    private void OnLogColumnItemsChanged(
      object? sender,
      System.Collections.Specialized.NotifyCollectionChangedEventArgs e
    )
    {
      if (e.NewItems != null)
        foreach (LogColumnItem newItem in e.NewItems)
          SubscribeLogColumnItem(newItem);

      NotifyActiveColumnsChanged();
    }

    private void SubscribeLogColumnItem(LogColumnItem item)
    {
      if (!_subscribedLogColumnItems.Add(item))
        return;
      item.PropertyChanged += (_, _) => NotifyActiveColumnsChanged();
    }

    private System.Threading.CancellationTokenSource? _activeColumnsCts;

    private void NotifyActiveColumnsChanged()
    {
      _activeColumnsCts?.Cancel();
      _activeColumnsCts = new System.Threading.CancellationTokenSource();
      var token = _activeColumnsCts.Token;

      _dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Background,
        new Action(() =>
        {
          if (!token.IsCancellationRequested)
            OnPropertyChanged(nameof(ActiveColumns));
        })
      );
    }

    private System.Threading.CancellationTokenSource? _statsRecalculationCts;

    private void NotifyFilteredEntriesChanged()
    {
      OnPropertyChanged(nameof(FilteredEntryCount));

      _statsRecalculationCts?.Cancel();
      _statsRecalculationCts = new System.Threading.CancellationTokenSource();
      var token = _statsRecalculationCts.Token;

      _dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Background,
        new Action(() =>
        {
          if (token.IsCancellationRequested)
            return;

          var filtered = _filteredEntries.Cast<PlayLogEntry>().ToList();
          var allEntriesIgnoringMode = Entries.Where(PassesNonModeFilters).ToList();

          _lastFilteredEntries = filtered;
          _contributionGraphViewModel.Recalculate(filtered, allEntriesIgnoringMode);

          _contributionChartViewModel.Recalculate(
            _contributionGraphViewModel.DailyHits,
            _contributionGraphViewModel.CurrentMonth
          );
          _playStatsChartViewModel.Recalculate(
            filtered,
            _contributionGraphViewModel.DailyStats,
            _contributionGraphViewModel.CurrentMonth
          );
        })
      );
    }

    public async Task LoadAsync()
    {
      if (IsLoading)
        return;
      IsLoading = true;
      StatusText = "Loading logs...";

      try
      {
        await _service.LoadAndCalculateAsync();
        StatusText =
          Entries.Count == 0
            ? "There is no play history."
            : $"Loaded {Entries.Count} play history entries.";
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogViewModel.LoadAsync failed: " + ex.Message, true);
        StatusText = "Failed to load.";
      }
      finally
      {
        IsLoading = false;
      }
    }

    public void Dispose()
    {
      _service.Dispose();
    }
  }
}
