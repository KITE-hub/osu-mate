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

    public List<int> HitErrors => _snapshot.HitErrors;

    public int HitErrorTotalCount => _snapshot.HitErrorTotalCount;

    public void Update(
      List<int> hitErrors,
      int hitErrorTotalCount,
      IReadOnlyDictionary<HitResult, double> hitWindows,
      bool isPlaying
    )
    {
      var errors = isPlaying ? hitErrors : [];
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

    public int GetJudgement(double offsetMs) =>
      HitJudgementHelper.GetJudgement(offsetMs, _snapshot.HitWindows);

    public double GetMaxWindow() => HitJudgementHelper.GetMaxWindow(_snapshot.HitWindows);

    public List<(int judgement, double msValue, double from, double to)> GetCenterLineSegments() =>
      HitJudgementHelper.GetCenterLineSegments(_snapshot.HitWindows);
  }
}
