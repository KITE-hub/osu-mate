using osu.Game.Rulesets.Scoring;
using OsuMate.Services.Osu;

namespace OsuMate.ViewModels
{
    public class URBarViewModel
    {
        // スレッドセーフなスナップショット保持
        private volatile List<int> _hitErrors = [];
        private volatile Dictionary<HitResult, double> _hitWindows = [];

        // 描画側が「データが更新された」を検知するためのフラグ
        private volatile bool _isDirty = false;

        /// <summary>描画スレッドが読み取る用のスナップショット</summary>
        public List<int> HitErrors => _hitErrors;

        /// <summary>
        /// バックグラウンドスレッドから呼ばれる更新メソッド。
        /// リストをコピーして保持することでスレッド安全を確保する。
        /// </summary>
        public void Update(List<int> hitErrors, Dictionary<HitResult, double> hitWindows, bool isPlaying)
        {
            // コピーを作成してから参照を差し替える
            var errorsCopy = isPlaying ? new List<int>(hitErrors) : [];
            var windowsCopy = new Dictionary<HitResult, double>(hitWindows);

            _hitErrors = errorsCopy;
            _hitWindows = windowsCopy;
            _isDirty = true;
        }

        /// <summary>描画側がフラグを消費するメソッド</summary>
        public bool ConsumeIsDirty()
        {
            if (!_isDirty) return false;
            _isDirty = false;
            return true;
        }

        public int GetJudgement(double offsetMs) => HitJudgementHelper.GetJudgement(offsetMs, _hitWindows);
        public double GetMaxWindow() => HitJudgementHelper.GetMaxWindow(_hitWindows);
        public List<(int judgement, double msValue, double from, double to)> GetCenterLineSegments()
            => HitJudgementHelper.GetCenterLineSegments(_hitWindows);
    }
}
