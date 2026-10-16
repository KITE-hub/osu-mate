using osu.Game.Rulesets.Scoring;
using OsuMate.Services.Osu;

namespace OsuMate.ViewModels
{
  public class URBarViewModel
  {
    private sealed record HitSnapshot(
      List<int> HitErrors,
      int HitErrorTotalCount,
      IReadOnlyDictionary<HitResult, double> HitWindows
    );

    private static readonly HitSnapshot EmptySnapshot = new(
      [],
      0,
      new Dictionary<HitResult, double>()
    );

    private volatile HitSnapshot _snapshot = EmptySnapshot;

    private volatile bool _isDirty = false;

    private static readonly List<int> NoHitErrors = [];

    private HitJudgementWindows _windows;
    private List<(int judgement, double msValue, double from, double to)> _centerLineSegments = [];

    private int _lastHitErrorVersion = -1;
    private bool _lastIsPlaying;
    private IReadOnlyDictionary<HitResult, double>? _lastHitWindows;

    public List<int> HitErrors => _snapshot.HitErrors;

    public int HitErrorTotalCount => _snapshot.HitErrorTotalCount;

    public void Update(
      List<int> hitErrors,
      int hitErrorTotalCount,
      int hitErrorVersion,
      IReadOnlyDictionary<HitResult, double> hitWindows,
      bool isPlaying
    )
    {
      if (
        hitErrorVersion == _lastHitErrorVersion
        && isPlaying == _lastIsPlaying
        && ReferenceEquals(hitWindows, _lastHitWindows)
      )
        return;

      _lastHitErrorVersion = hitErrorVersion;
      _lastIsPlaying = isPlaying;
      if (!ReferenceEquals(_snapshot.HitWindows, hitWindows))
      {
        _windows = new HitJudgementWindows(hitWindows);
        _centerLineSegments =
        [
          .. HitJudgementHelper.GetCenterLineSegments(hitWindows).OrderBy(segment => segment.from),
        ];
      }

      _lastHitWindows = hitWindows;

      var errors = isPlaying ? hitErrors : NoHitErrors;
      var totalCount = isPlaying ? hitErrorTotalCount : 0;

      _snapshot = new HitSnapshot(errors, totalCount, hitWindows);
      _isDirty = true;
    }

    public bool ConsumeIsDirty()
    {
      if (!_isDirty)
        return false;
      _isDirty = false;
      return true;
    }

    public int GetJudgement(double offsetMs) => _windows.GetJudgement(offsetMs);

    public double GetMaxWindow() => _windows.Max;

    public List<(int judgement, double msValue, double from, double to)> GetCenterLineSegments() =>
      _centerLineSegments;
  }
}
