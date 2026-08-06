using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// SR / pp が未計算のエントリを検出し、バックグラウンドで計算してエントリに付与する。
    /// </summary>
    public class PlayLogSrPpEnricher
    {
        private readonly OsuMemoryService _memory;
        private readonly BeatmapPathResolver _pathResolver;
        private readonly PlayLogRepository _repository;

        public PlayLogSrPpEnricher(
            OsuMemoryService memory,
            BeatmapPathResolver pathResolver,
            PlayLogRepository repository)
        {
            _memory = memory;
            _pathResolver = pathResolver;
            _repository = repository;
        }

        /// <summary>
        /// Entries の中で SR/pp が未計算のエントリをバックグラウンドで計算する。
        /// OsuMemoryService がディレクトリを取得するまで最大10秒待機する。
        /// </summary>
        public async Task CalculateMissingSrPpAsync(
            Dispatcher dispatcher,
            ObservableCollection<PlayLogEntry> entries,
            Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map)
        {
            // OsuMemoryService がディレクトリを取得するまで最大10秒待機
            for (int i = 0; i < 100 && !_memory.IsDirectoryLoaded; i++)
                await Task.Delay(100);

            if (!_memory.IsDirectoryLoaded) return;

            // ObservableCollectionへの列挙競合エラーを防ぐため、UIスレッド上でスナップショットを取得する。
            // Beatmap識別子(ID/MD5)があり、計算未失敗のエントリが対象。
            // 完走済みかどうかは問わない(中断プレイのSRも対象にするが、ppは書き込み側で除外する)。
            var targets = await dispatcher.InvokeAsync(() =>
                entries.Where(e => e.StarRating == null &&
                                   !e.IsCalculationFailed &&
                                   (e.BeatmapId > 0 || !string.IsNullOrEmpty(e.BeatmapMd5)))
                       .ToList());
            if (targets.Count == 0) return;

            // osu!.db を MD5→BeatmapInfo のキャッシュとして読み込む（MD5検索用）
            // 起動時にキャッシュ済みであればそちらを使い、未キャッシュの場合のみ読み込む
            if (md5Map == null)
            {
                try
                {
                    var osuDbPath = Path.Combine(_memory.OsuDirectory, "osu!.db");
                    if (File.Exists(osuDbPath))
                        md5Map = OsuMate.Services.StableDb.OsuDbReader.ReadBeatmaps(osuDbPath);
                }
                catch (Exception ex)
                {
                    LogUtils.DebugLogger("PlayLogSrPpEnricher.CalculateMissingSrPpAsync: osu!.db read failed: " + ex.Message, true);
                }
            }

            // 同じ日付(=同じJSONファイル)の分をまとめて1回だけ書き込む。
            // エントリごとにSaveEntryを呼ぶと、同じ日に何十件もプレイがある場合に
            // 同一ファイルを繰り返し読み書きしてしまうため、日付単位でグループ化する。
            // グループ(1日分)が終わるたびに書き込むので、途中で例外が起きても
            // それまでに完了した日付分は失われない。
            foreach (var dateGroup in targets.GroupBy(e => e.PlayedAt.Date))
            {
                var toSave = new List<PlayLogEntry>();

                foreach (var entry in dateGroup)
                {
                    try
                    {
                        var (sr, pp) = CalculateSrPpForEntry(entry, md5Map);
                        if (sr == null)
                        {
                            // 計算に必要な譜面データが無い等の理由で失敗した場合はフラグを立てて保存し、次回からスキップする
                            // entry は Entries にバインド済みの可能性があるため、代入はUIスレッドで行う。
                            await dispatcher.InvokeAsync(() => { entry.IsCalculationFailed = true; });
                            toSave.Add(entry);
                            continue;
                        }

                        // entry は Entries（UIにバインド済み）の要素であり得るため、プロパティ代入は必ずUIスレッドで行う。
                        // pp は最後までプレイしきったスコア(IsCompleted=true)にのみ意味を持つ値のため、
                        // 中断プレイでは SR のみ反映し、pp は書き込まない(null のまま)。
                        await dispatcher.InvokeAsync(() =>
                        {
                            entry.StarRating = sr;
                            if (entry.IsCompleted)
                                entry.Pp = pp;
                        });
                        toSave.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        LogUtils.DebugLogger($"PlayLogSrPpEnricher: SR/pp calc failed for BeatmapId={entry.BeatmapId}: {ex.Message}", true);
                    }
                }

                // 更新をファイルにも反映（I/Oなのでバックグラウンドのままでよい）
                if (toSave.Count > 0)
                    _repository.SaveEntries(toSave);
            }
        }

        public (double? sr, double? pp) CalculateSrPpForEntry(
            PlayLogEntry entry,
            Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map = null)
        {
            // 1. MD5 が分かれば osu!.db のフォルダ/ファイル名から直接パスを引く（unranked対応）
            // 2. BeatmapId が分かれば md5Map を BeatmapID でグルーピングし直してO(1)で引く
            // 3. それでも見つからない場合のみ、Songs フォルダ全体をスキャンする（フォールバック）
            var beatmapPath = _pathResolver.FindBeatmapPathByMd5(entry.BeatmapMd5, md5Map)
                           ?? _pathResolver.FindBeatmapPathById(entry.BeatmapId, md5Map)
                           ?? _pathResolver.FindBeatmapPath(entry.BeatmapId);
            if (beatmapPath == null) return (null, null);

            var mods = (string.IsNullOrEmpty(entry.ModsString) || entry.ModsString == "NM")
                ? Array.Empty<string>()
                : entry.ModsString.Split(',').Select(m => m.Trim().ToLower()).ToArray();

            var calculator = new PpCalculator(beatmapPath, entry.Mode);

            var hits = new HitsResult
            {
                HitGeki = entry.CountGeki,
                Hit300  = entry.Count300,
                HitKatu = entry.CountKatu,
                Hit100  = entry.Count100,
                Hit50   = entry.Count50,
                HitMiss = entry.CountMiss,
                Combo   = entry.MaxCombo,
                Score   = entry.TotalScore,
            };

            // Accuracy を実際の Hits から計算する
            double accuracy = OsuUtils.CalculateAccuracy(hits, entry.Mode);

            var args = new CalculateArgs
            {
                Mods                   = mods,
                Time                   = int.MaxValue,
                Combo                  = entry.MaxCombo,
                Score                  = entry.TotalScore,
                Accuracy               = accuracy * 100, // パーセント値として渡す
                HitErrors              = [],
            };

            var data = calculator.Calculate(args, false, true, hits);
            double? sr = data.DifficultyAttributes?.StarRating;
            double? pp = data.CurrentPerformanceAttributes?.Total;
            return (sr, pp);
        }
    }
}
