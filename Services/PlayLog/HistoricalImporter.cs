using System;
using System.Collections.Generic;
using System.IO;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// 起動時の osu!.db / scores.db 一括取り込みを担当する。
    /// </summary>
    public class HistoricalImporter
    {
        private readonly OsuMemoryService _memory;
        private readonly GlobalConfig _config;

        public HistoricalImporter(OsuMemoryService memory)
        {
            _memory = memory;
            _config = ConfigUtils.LoadGlobalConfig();
        }

        /// <summary>
        /// osu!.db / scores.db を読み込んで PlayLogEntry リストを返す。
        /// md5Map と scoresDbPath は呼び出し元でキャッシュして再利用する。
        /// </summary>
        public List<PlayLogEntry> LoadFromLocalOsuData(
            out Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map,
            out string? scoresDbPath)
        {
            md5Map = null;
            scoresDbPath = null;
            var list = new List<PlayLogEntry>();
            try
            {
                // osu! プロセスから取得したディレクトリを優先し、
                // 未取得の場合は既知の候補ディレクトリを順に探す
                var candidates = new List<string>();

                // Config で手動設定されたパスを最優先
                if (!string.IsNullOrWhiteSpace(_config.OsuExeDirectory))
                    candidates.Add(_config.OsuExeDirectory);

                // osu! プロセスから取得したパス
                if (!string.IsNullOrWhiteSpace(_memory.OsuDirectory))
                    candidates.Add(_memory.OsuDirectory);

                // exe 隣のディレクトリ
                candidates.Add(AppDomain.CurrentDomain.BaseDirectory);

                // exe の実行ファイルの実際の場所
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dir) && !candidates.Contains(dir))
                        candidates.Add(dir);
                }

                // osu! stable の標準インストール場所 (%AppData%\osu!)
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var osuAppData = Path.Combine(appData, "osu!");
                if (!candidates.Contains(osuAppData))
                    candidates.Add(osuAppData);

                LogUtils.DebugLogger($"HistoricalImporter: DB search candidates: {string.Join(", ", candidates)}", true);

                string? osuDbPath = null;
                foreach (var dir in candidates)
                {
                    var o = Path.Combine(dir, "osu!.db");
                    var s = Path.Combine(dir, "scores.db");
                    if (File.Exists(o) && File.Exists(s))
                    {
                        osuDbPath = o;
                        scoresDbPath = s;
                        break;
                    }
                }
                if (osuDbPath == null || scoresDbPath == null)
                {
                    LogUtils.DebugLogger("HistoricalImporter: osu!.db or scores.db not found in any candidate directory", true);
                    return list;
                }
                LogUtils.DebugLogger($"HistoricalImporter: Reading DB from {osuDbPath}", true);

                md5Map = OsuMate.Services.StableDb.OsuDbReader.ReadBeatmaps(osuDbPath);
                var scores = OsuMate.Services.StableDb.ScoresDbReader.ReadScores(scoresDbPath);
                foreach (var score in scores)
                {
                    if (!md5Map.TryGetValue(score.Md5Hash, out var beatmap)) continue;
                    list.Add(PlayLogEntryFactory.FromScoresDbScore(score, beatmap));
                }
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("HistoricalImporter.LoadFromLocalOsuData failed: " + ex.Message, true);
            }

            return list;
        }
    }
}
