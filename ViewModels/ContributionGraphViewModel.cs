using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OsuMate.Models;
using OsuMate.Services.PlayLog;

namespace OsuMate.ViewModels
{
  public class ContributionGraphViewModel : ObservableBase
  {
    private const int ActivityLevelCount = 4;

    private readonly PlayLogAggregationService _aggregationService;
    private readonly PlayStatsAggregationService _playStatsAggregationService;

    private IReadOnlyDictionary<DateOnly, int> _cachedDailyHits = new Dictionary<DateOnly, int>();

    public IReadOnlyDictionary<DateOnly, int> DailyHits => _cachedDailyHits;

    private IReadOnlyDictionary<DateOnly, DailyPlayStats> _cachedDailyStats =
      new Dictionary<DateOnly, DailyPlayStats>();

    public IReadOnlyDictionary<DateOnly, DailyPlayStats> DailyStats => _cachedDailyStats;

    private int _cachedMaxHits = 1;

    private DateOnly _currentMonth;

    public DateOnly CurrentMonth
    {
      get => _currentMonth;
      private set
      {
        if (_currentMonth == value)
          return;
        _currentMonth = value;
        OnPropertyChanged();

        if (_selectedMonth != value)
        {
          _selectedMonth = value;
          OnPropertyChanged(nameof(SelectedMonth));
        }
        RebuildDays();
        UpdateNavigationState();
      }
    }

    public ObservableCollection<DateOnly> AvailableMonths { get; } = new();

    private DateOnly _selectedMonth;

    public DateOnly SelectedMonth
    {
      get => _selectedMonth;
      set
      {
        if (_selectedMonth == value)
          return;
        _selectedMonth = value;
        OnPropertyChanged();

        CurrentMonth = value;
      }
    }

    private bool _canGoToPreviousMonth;

    public bool CanGoToPreviousMonth
    {
      get => _canGoToPreviousMonth;
      private set
      {
        if (_canGoToPreviousMonth == value)
          return;
        _canGoToPreviousMonth = value;
        OnPropertyChanged();
      }
    }

    private bool _canGoToNextMonth;

    public bool CanGoToNextMonth
    {
      get => _canGoToNextMonth;
      private set
      {
        if (_canGoToNextMonth == value)
          return;
        _canGoToNextMonth = value;
        OnPropertyChanged();
      }
    }

    public ICommand PreviousMonthCommand { get; }

    public ICommand NextMonthCommand { get; }

    public ObservableCollection<ContributionDay> Days { get; } = new();

    public ContributionGraphViewModel(
      PlayLogAggregationService aggregationService,
      PlayStatsAggregationService playStatsAggregationService
    )
    {
      _aggregationService = aggregationService;
      _playStatsAggregationService = playStatsAggregationService;
      _currentMonth = ThisRealMonth();

      _selectedMonth = _currentMonth;

      PreviousMonthCommand = new RelayCommand(
        _ => CurrentMonth = CurrentMonth.AddMonths(-1),
        _ => CanGoToPreviousMonth
      );
      NextMonthCommand = new RelayCommand(
        _ => CurrentMonth = CurrentMonth.AddMonths(1),
        _ => CanGoToNextMonth
      );

      UpdateNavigationState();
    }

    public void Recalculate(
      IEnumerable<PlayLogEntry> filteredEntries,
      IEnumerable<PlayLogEntry> allEntriesIgnoringMode
    )
    {
      var filteredList = filteredEntries as ICollection<PlayLogEntry> ?? filteredEntries.ToList();

      _cachedDailyHits = _aggregationService.AggregateDailyHits(filteredList);
      _cachedDailyStats = _playStatsAggregationService
        .AggregateDailyStats(filteredList)
        .ToDictionary(s => s.Date);
      _cachedMaxHits = _cachedDailyHits.Count > 0 ? _cachedDailyHits.Values.Max() : 1;
      RebuildAvailableMonths(allEntriesIgnoringMode);
      RebuildDays();
    }

    private static DateOnly ThisRealMonth()
    {
      var now = DateTime.Now;
      return new DateOnly(now.Year, now.Month, 1);
    }

    private void UpdateNavigationState()
    {
      CanGoToPreviousMonth = AvailableMonths.Count > 0 && CurrentMonth > AvailableMonths[0];
      CanGoToNextMonth = CurrentMonth < ThisRealMonth();
    }

    private void RebuildAvailableMonths(IEnumerable<PlayLogEntry> allEntriesIgnoringMode)
    {
      var thisMonth = ThisRealMonth();

      var entriesList =
        allEntriesIgnoringMode as ICollection<PlayLogEntry> ?? allEntriesIgnoringMode.ToList();

      DateOnly earliest;
      if (entriesList.Count == 0)
      {
        earliest = thisMonth;
      }
      else
      {
        var minDate = entriesList.Min(e => e.PlayedAt);
        earliest = new DateOnly(minDate.Year, minDate.Month, 1);
      }

      AvailableMonths.Clear();
      var m = earliest;
      while (m <= thisMonth)
      {
        AvailableMonths.Add(m);
        m = m.AddMonths(1);
      }

      if (!AvailableMonths.Contains(_currentMonth))
      {
        _currentMonth = thisMonth;
        _selectedMonth = thisMonth;
        OnPropertyChanged(nameof(CurrentMonth));
      }

      OnPropertyChanged(nameof(SelectedMonth));

      UpdateNavigationState();
    }

    private void RebuildDays()
    {
      Days.Clear();

      var today = DateOnly.FromDateTime(DateTime.Now);
      var targetYear = _currentMonth.Year;
      var targetMonth = _currentMonth.Month;

      var daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
      var firstDayOfMonth = new DateOnly(targetYear, targetMonth, 1);

      var startOffset = (int)firstDayOfMonth.DayOfWeek;

      for (int i = 0; i < startOffset; i++)
        Days.Add(ContributionDay.Placeholder);

      for (int day = 1; day <= daysInMonth; day++)
      {
        var date = new DateOnly(targetYear, targetMonth, day);

        if (date > today)
          break;

        var hits = _cachedDailyHits.GetValueOrDefault(date);
        var level = CalculateLevel(hits, _cachedMaxHits);
        var isToday = date == today;
        _cachedDailyStats.TryGetValue(date, out var dailyStats);
        Days.Add(
          new ContributionDay(
            date,
            hits,
            level,
            isToday,
            dailyStats?.StarRating ?? MetricStat.Empty,
            dailyStats?.Pp ?? MetricStat.Empty,
            dailyStats?.Accuracy ?? MetricStat.Empty
          )
        );
      }
    }

    private static int CalculateLevel(int hits, int maxHits)
    {
      if (hits <= 0 || maxHits <= 0)
        return 0;

      var ratio = (double)hits / maxHits;
      var level = (int)Math.Ceiling(ratio * ActivityLevelCount);
      return Math.Clamp(level, 1, ActivityLevelCount);
    }

    private sealed class RelayCommand : ICommand
    {
      private readonly Action<object?> _execute;
      private readonly Func<object?, bool>? _canExecute;

      public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
      {
        _execute = execute;
        _canExecute = canExecute;
      }

      public event EventHandler? CanExecuteChanged
      {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
      }

      public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

      public void Execute(object? parameter) => _execute(parameter);
    }
  }
}
