using OsuMate.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// プレイ履歴(<see cref="PlayLogEntry"/>)の追加・pp確定・完走確定を監視し、
    /// 現在の譜面に対する自己ベストpp（Best pp）を再計算・キャッシュするドメインサービス
    /// </summary>
    public class BestPpTracker
    {
        private readonly PlayLogService _playLogService;
        private readonly ObservableCollection<string> _targetPlayerNames;
        private readonly HashSet<PlayLogEntry> _trackedEntries = [];

        private string _lastBeatmapMd5 = "";

        /// <summary>直近に再計算されたBest pp。InGameOverlay等、毎tickの参照用にキャッシュしておく。</summary>
        public double? CachedBestPp { get; private set; }

        /// <summary>Best ppが再計算されたときに発火する。呼び出し元スレッドは特定しない。</summary>
        public event Action<double?>? BestPpChanged;

        public BestPpTracker(PlayLogService playLogService, ObservableCollection<string> targetPlayerNames)
        {
            _playLogService = playLogService;
            _targetPlayerNames = targetPlayerNames;

            // 対象プレイヤー名リストの編集を即座に拾って再計算する。
            _targetPlayerNames.CollectionChanged += (_, _) => Refresh(_lastBeatmapMd5);

            _playLogService.Entries.CollectionChanged += OnEntriesChanged;
            foreach (var entry in _playLogService.Entries) TrackEntry(entry);
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (PlayLogEntry entry in e.NewItems)
                    TrackEntry(entry);

            Refresh(_lastBeatmapMd5);
        }

        private void TrackEntry(PlayLogEntry entry)
        {
            if (!_trackedEntries.Add(entry)) return;

            // Pp は基本コミット時に確定済みだが、稀に非同期(PlayLogSrPpEnricher)で後から確定する場合がある。
            // IsCompleted は中断プレイのコミット直後は false のことがあり、結果画面確定時に true へ更新される
            // どちらの変化もBest ppの対象条件に関わるため拾う
            entry.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PlayLogEntry.Pp) || args.PropertyName == nameof(PlayLogEntry.IsCompleted))
                    Refresh(_lastBeatmapMd5);
            };
        }

        /// <summary>
        /// 指定した譜面（md5）が前回計算時と異なる場合のみBest ppを再計算する。
        /// 呼び出し元（毎tickのUI更新ループ等）が譜面変更を検知するたびに呼んでよい。
        /// </summary>
        public void RefreshIfChanged(string beatmapMd5)
        {
            if (beatmapMd5 == _lastBeatmapMd5) return;
            Refresh(beatmapMd5);
        }

        /// <summary>指定した譜面（md5）に対するBest ppを再計算し、キャッシュ・イベント通知する。</summary>
        public void Refresh(string beatmapMd5)
        {
            _lastBeatmapMd5 = beatmapMd5;
            var bestPp = BestPpCalculator.GetBestPp(_playLogService.Entries, beatmapMd5, _targetPlayerNames);
            CachedBestPp = bestPp;
            BestPpChanged?.Invoke(bestPp);
        }
    }
}
