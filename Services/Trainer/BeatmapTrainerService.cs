using OsuMate.Utils;
using System.IO;
using System.IO.Compression;

namespace OsuMate.Services.Trainer
{
    /// <summary>
    /// 現在の osu! 譜面に Rate（再生速度）を適用した新しい譜面を生成・エクスポートする。
    /// </summary>
    public class BatchGenerationRequest
    {
        public decimal Rate { get; set; }
        public decimal? ArOverride { get; set; }
        public decimal? OdOverride { get; set; }
        public decimal? HpOverride { get; set; }
        public decimal? CsOverride { get; set; }
    }

    public class BeatmapTrainerService
    {
        private readonly OsuMemoryService _memory;

        internal const decimal DifficultyChangeThreshold = 0.001M;

        public BeatmapTrainerService(OsuMemoryService memory)
        {
            _memory = memory;
        }

        // ============================================================
        //  譜面パス解決
        // ============================================================

        /// <summary>
        /// 現在 osu! で選択中の譜面パスを返す。
        /// osutrainer 生成済み譜面の場合は同フォルダの元譜面を探して返す。
        /// 取得できなければ null。
        /// </summary>
        public string? GetCurrentBeatmapPath()
        {
            if (!_memory.IsDirectoryLoaded) return null;
            var beatmap = _memory.GetBaseAddressSnapshot().Beatmap;
            string? folder = beatmap.FolderName?.Trim();
            string? file   = beatmap.OsuFileName?.Trim();
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file)) return null;

            try
            {
                string path = Path.Combine(_memory.SongsPath, folder, file);
                return File.Exists(path) ? path : null;
            }
            catch (Exception ex)
            {
                // folder/file は osu! プロセスのメモリから直接読んだ生文字列であり、
                // 譜面切り替え中などに不正な文字を含むことがある（Path.Combine が ArgumentException を投げる）。
                LogUtils.DebugLogger($"BeatmapTrainerService.GetCurrentBeatmapPath failed: {ex.Message}", true);
                return null;
            }
        }

        /// <summary>
        /// osutrainer 生成済み譜面のとき、同フォルダにある元の .osu を探して返す。
        /// 見つからなければ null。
        ///
        /// 特定の優先順位:
        ///  1. 生成時に埋め込まれた元ファイル名（<see cref="OsuBeatmapFile.SourceOsuFileName"/>）から直接特定する。
        ///     最も確実な方法で、今後生成される譜面はすべてこの情報を持つ。
        ///  2. 埋め込み情報が無い場合（本修正より前のバージョンで生成された譜面など）は、
        ///     Version 名の前方一致でフォルダ内の候補と照合する（生成後の Version は
        ///     "{元のVersion} {rate}x (...)" ）。複数の候補が前方一致する
        ///     場合は、最も長く一致した（＝最も具体的な）ものを採用する
        ///  3. どちらの方法でも一意に特定できない場合は、null を返す
        /// </summary>
        public static string? FindOriginalMap(string osutrainerMapPath)
        {
            string dir = Path.GetDirectoryName(osutrainerMapPath)!;

            OsuBeatmapFile? trainerMap = null;
            try { trainerMap = OsuBeatmapFile.Load(osutrainerMapPath); }
            catch (Exception ex)
            {
                LogUtils.DebugLogger($"[Trainer] Failed to load generated beatmap: {ex.Message}", true);
                return null;
            }

            string[] candidates;
            try { candidates = Directory.GetFiles(dir, "*.osu").OrderBy(f => f).ToArray(); }
            catch (Exception ex)
            {
                LogUtils.DebugLogger($"[Trainer] Failed to enumerate folders: {ex.Message}", true);
                return null;
            }

            // 1. 埋め込まれたファイル名から直接特定する
            if (!string.IsNullOrEmpty(trainerMap.SourceOsuFileName))
            {
                string candidatePath = Path.Combine(dir, trainerMap.SourceOsuFileName);
                if (File.Exists(candidatePath))
                {
                    try
                    {
                        var candidate = OsuBeatmapFile.Load(candidatePath);
                        if (!candidate.IsOsuTrainerMap) return candidatePath;
                    }
                    catch (Exception ex)
                    {
                        LogUtils.DebugLogger($"[Trainer] Failed to load embedded original beatmap: {ex.Message}", true);
                    }
                }
            }

            // 2. Version 名の前方一致で推定する（埋め込み情報が無い旧生成物向けの後方互換フォールバック）
            string? bestMatch = null;
            int     bestLength = -1;
            foreach (var osuFile in candidates)
            {
                if (string.Equals(osuFile, osutrainerMapPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var candidate = OsuBeatmapFile.Load(osuFile);
                    if (candidate.IsOsuTrainerMap) continue;
                    if (string.IsNullOrEmpty(candidate.Version)) continue;

                    if (trainerMap.Version.StartsWith(candidate.Version + " ", StringComparison.Ordinal)
                        && candidate.Version.Length > bestLength)
                    {
                        bestMatch  = osuFile;
                        bestLength = candidate.Version.Length;
                    }
                }
                catch (Exception ex)
                {
                    LogUtils.DebugLogger($"[Trainer] Failed to load candidate beatmap ({osuFile}): {ex.Message}", true);
                }
            }

            // 3. 一意に特定できなければ、誤った譜面を使うより「見つからない」を返す
            return bestMatch;
        }

        // ============================================================
        //  譜面生成
        // ============================================================

        /// <summary>
        /// 複数のRate設定に基づき、一括で譜面を生成して1つの.oszとしてエクスポートする。
        /// 一部のRateで生成に失敗しても、成功した分は破棄せずに.oszへ含める。
        /// </summary>
        public async Task GenerateBeatmapsBatchAsync(
            string beatmapPath,
            IEnumerable<BatchGenerationRequest> requests,
            bool adjustPitchWithSpeed,
            Action<string>? progress = null)
        {
            await Task.Run(() =>
            {
                progress?.Invoke("Loading beatmap...");
                var original = OsuBeatmapFile.Load(beatmapPath);
                string songDir = Path.GetDirectoryName(beatmapPath)!;

                var osuFilesToAdd = new List<(string tempOsuPath, string newOsuFilename)>();
                var failures = new List<(decimal rate, string reason)>();

                try
                {
                    foreach (var req in requests)
                    {
                        string tempOsuPath = "";
                        try
                        {
                            if (req.Rate <= 0)
                                throw new ArgumentOutOfRangeException(nameof(req.Rate), req.Rate, "rate must be greater than zero.");

                            // --- メタデータ構築 ---
                            string bpmStr = (original.DominantBpm * req.Rate).ToString("0");
                            string rateStr = req.Rate.ToString("0.##");
                            string newVersion = $"{original.Version} {rateStr}x ({bpmStr}bpm)";

                            var diffSuffixes = new List<string>();
                            if (req.ArOverride.HasValue && original.ApproachRate >= 0 && Math.Abs(req.ArOverride.Value - original.ApproachRate) > DifficultyChangeThreshold)
                                diffSuffixes.Add($"AR{req.ArOverride:F1}");
                            if (req.OdOverride.HasValue && original.OverallDifficulty >= 0 && Math.Abs(req.OdOverride.Value - original.OverallDifficulty) > DifficultyChangeThreshold)
                                diffSuffixes.Add($"OD{req.OdOverride:F1}");
                            if (req.HpOverride.HasValue && original.HPDrainRate >= 0 && Math.Abs(req.HpOverride.Value - original.HPDrainRate) > DifficultyChangeThreshold)
                                diffSuffixes.Add($"HP{req.HpOverride:F1}");
                            if (req.CsOverride.HasValue && original.CircleSize >= 0 && Math.Abs(req.CsOverride.Value - original.CircleSize) > DifficultyChangeThreshold)
                                diffSuffixes.Add($"CS{req.CsOverride:F1}");
                            if (diffSuffixes.Count > 0)
                                newVersion += $" [{string.Join(" ", diffSuffixes)}]";

                            // 新しい AudioFilename
                            string audioBase = Path.GetFileNameWithoutExtension(original.AudioFilename);
                            string newAudioName = $"{audioBase} {req.Rate:0.000}x";
                            if (adjustPitchWithSpeed && Math.Abs(req.Rate - 1M) > 0.001M)
                                newAudioName += $" (pitch {(req.Rate < 1 ? "lowered" : "raised")})";
                            newAudioName += ".mp3";

                            // 新しい .osu ファイル名
                            string artist = OsuBeatmapFile.NormalizeForFilename(original.Artist);
                            string title = OsuBeatmapFile.NormalizeForFilename(original.Title);
                            string creator = OsuBeatmapFile.NormalizeForFilename(original.Creator);
                            string diffName = OsuBeatmapFile.NormalizeForFilename(newVersion);
                            string newOsuFilename = $"{artist} - {title} ({creator}) [{diffName}].osu";

                            // tags
                            var tags = new List<string>(original.Tags);
                            if (!tags.Contains("osutrainer")) tags.Add("osutrainer");

                            // 音声ファイル生成
                            string newAudioPath = Path.Combine(songDir, newAudioName);
                            bool needMp3 = !File.Exists(newAudioPath);

                            if (needMp3)
                            {
                                progress?.Invoke($"Generating audio... ({req.Rate:0.##}x)");
                                string inAudio = Path.Combine(songDir, original.AudioFilename);
                                SongSpeedChanger.GenerateAudioFile(inAudio, newAudioPath, req.Rate, adjustPitchWithSpeed);
                            }

                            // .osu ファイル生成
                            progress?.Invoke($"Generating beatmap... ({req.Rate:0.##}x)");
                            tempOsuPath = Path.GetTempFileName();

                            // OsuBeatmapFile は SaveWithRate で自身を書き換えてしまうため、クローンまたは元の状態を維持する必要がある。
                            // 一番安全なのは都度ロードし直すこと。
                            var mapToSave = OsuBeatmapFile.Load(beatmapPath);
                            mapToSave.Version = newVersion;
                            mapToSave.AudioFilename = newAudioName;
                            mapToSave.Tags = tags;
                            mapToSave.SaveWithRate(tempOsuPath, req.Rate, req.ArOverride, req.OdOverride, req.HpOverride, req.CsOverride);

                            osuFilesToAdd.Add((tempOsuPath, newOsuFilename));
                        }
                        catch (Exception ex)
                        {
                            // 1件の失敗で他の成功済みRateまで巻き添えにしないよう、
                            // ここで握りつぶして残りのRateの処理を続行する。
                            LogUtils.DebugLogger($"[Trainer] Failed to generate Rate {req.Rate:0.##}x: {ex.Message}", true);
                            failures.Add((req.Rate, ex.Message));
                            if (!string.IsNullOrEmpty(tempOsuPath))
                            {
                                try { if (File.Exists(tempOsuPath)) File.Delete(tempOsuPath); }
                                catch (Exception delEx) { LogUtils.DebugLogger($"[Trainer] Failed to delete temporary file: {delEx.Message}"); }
                            }
                        }
                    }

                    if (osuFilesToAdd.Count == 0)
                    {
                        string detail = string.Join(" / ", failures.Select(f => $"{f.rate:0.##}x: {f.reason}"));
                        throw new InvalidOperationException($"Failed to generate for all Rates. {detail}");
                    }

                    progress?.Invoke("Creating .osz...");
                    AddNewBeatmapsToSongFolder(songDir, osuFilesToAdd);

                    if (failures.Count > 0)
                    {
                        string failedRates = string.Join(", ", failures.Select(f => $"{f.rate:0.##}x"));
                        progress?.Invoke($"Done! (failed: {failedRates})");
                    }
                    else
                    {
                        progress?.Invoke("Done!");
                    }
                }
                finally
                {
                    // cleanup
                    foreach (var (tempOsuPath, _) in osuFilesToAdd)
                    {
                        try { if (File.Exists(tempOsuPath)) File.Delete(tempOsuPath); }
                        catch (Exception ex) { LogUtils.DebugLogger($"[Trainer] Failed to delete temporary file: {ex.Message}"); }
                    }
                }
            });
        }

        // ============================================================
        //  private helpers
        // ============================================================

        private static void AddNewBeatmapsToSongFolder(
            string songDir,
            IEnumerable<(string tempOsuPath, string newOsuFilename)> osuFiles)
        {
            // songDir はディレクトリ名であり拡張子という概念が無いため、
            // GetFileNameWithoutExtension ではなく GetFileName を使う
            // （フォルダ名にピリオドが含まれる場合に名前が欠けるのを防ぐ）。
            string oszName = Path.GetFileName(songDir) + ".osz";
            string oszPath = Path.Combine(Path.GetTempPath(), oszName);

            if (File.Exists(oszPath)) File.Delete(oszPath);

            try { ZipFile.CreateFromDirectory(songDir, oszPath); }
            catch (Exception ex)
            {
                LogUtils.DebugLogger($"[Trainer] .osz creation error: {ex.Message}", true);
                throw;
            }

            // 新しく生成した音声ファイルは songDir 内に直接書き込まれており、
            // 上の CreateFromDirectory で songDir を丸ごと zip 化した時点で
            // 既にアーカイブへ含まれている。ここで改めて追加すると同名エントリが
            // 重複してしまうため、songDir の外（一時ファイル）にある .osu だけを追加する。
            using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Update))
            {
                foreach (var osu in osuFiles)
                {
                    archive.CreateEntryFromFile(osu.tempOsuPath, osu.newOsuFilename);
                }
            }

            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = oszPath;
            proc.StartInfo.UseShellExecute = true;
            try { proc.Start(); }
            catch (Exception ex)
            {
                LogUtils.DebugLogger($"[Trainer] .osz launch error: {ex.Message}", true);
                throw new InvalidOperationException(
                    "Failed to open .osz file.\nPlease check if .osz files are associated with osu!.", ex);
            }
        }
    }
}
