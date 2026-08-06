using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
    public class PlayLogRepository
    {
        private readonly GlobalConfig _config;
        private readonly object _saveFileLock = new();

        /// <summary>
        /// Settings画面でLog Directoryが変更された際、再起動を待たず即座に反映するための上書き値。
        /// null の場合は _config.LogOutputDir（起動時に読み込んだ設定）を使う。
        /// </summary>
        private string? _liveOverrideDir;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public PlayLogRepository()
        {
            _config = ConfigUtils.LoadGlobalConfig();
        }

        public string LogOutputDir
        {
            get
            {
                var dir = _liveOverrideDir ?? _config.LogOutputDir;
                if (string.IsNullOrWhiteSpace(dir))
                    dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayLogs");
                return dir;
            }
        }

        /// <summary>
        /// Settings画面でLog Directoryが変更された際に、このRepositoryが実際に使う保存先を
        /// 即座に切り替える。空文字/nullを渡すと既定値（起動時のConfigまたはPlayLogsフォルダ）に戻る。
        /// </summary>
        public void SetLogOutputDirOverride(string? directory)
        {
            _liveOverrideDir = string.IsNullOrWhiteSpace(directory) ? null : directory;
        }

        /// <summary>
        /// Log Directory 変更時に、既存のプレイ履歴JSONを新しい保存先へ移動する。
        /// アプリ再起動時には「変更前のディレクトリ」を覚えていないため、この移動は
        /// 設定変更のタイミングで即座に行う必要がある（さもないと、変更前のフォルダにある
        /// JSONが「存在しない」扱いになり、プレイ履歴が消えたように見えてしまう）。
        /// 移動先に同名の日付ファイルが既にある場合は、上書きせずDedupeKeyで統合する。
        /// </summary>
        public void MigrateLogFiles(string oldDir, string newDir)
        {
            if (string.IsNullOrWhiteSpace(oldDir) || string.IsNullOrWhiteSpace(newDir)) return;

            string oldFull, newFull;
            try
            {
                oldFull = Path.GetFullPath(oldDir);
                newFull = Path.GetFullPath(newDir);
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogRepository.MigrateLogFiles: Failed to resolve path: " + ex.Message, true);
                return;
            }

            if (string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(oldFull)) return;

            lock (_saveFileLock)
            {
                try
                {
                    Directory.CreateDirectory(newFull);
                }
                catch (Exception ex)
                {
                    LogUtils.DebugLogger($"PlayLogRepository.MigrateLogFiles: Failed to create {newFull}: {ex.Message}", true);
                    return;
                }

                foreach (var file in Directory.GetFiles(oldFull, "*.json"))
                {
                    var destPath = Path.Combine(newFull, Path.GetFileName(file));

                    try
                    {
                        if (File.Exists(destPath))
                        {
                            // 移動先に既に同じ日付のファイルがある場合は、片方を失わないようDedupeKeyで統合する
                            var merged = LoadFromFile(file)
                                .Concat(LoadFromFile(destPath))
                                .GroupBy(e => e.DedupeKey)
                                .Select(g => g.Last())
                                .OrderBy(e => e.PlayedAt)
                                .ToList();

                            string json = JsonSerializer.Serialize(merged, JsonOpts);
                            string tempPath = destPath + ".tmp";
                            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                            File.Move(tempPath, destPath, overwrite: true);
                            File.Delete(file);
                        }
                        else
                        {
                            File.Move(file, destPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 1ファイルの移動失敗で他のファイルの移動まで止めない
                        LogUtils.DebugLogger($"PlayLogRepository.MigrateLogFiles: Failed to move {file}: {ex.Message}", true);
                    }
                }
            }
        }

        public void SaveEntry(PlayLogEntry entry, string? oldDedupeKey = null)
        {
            try
            {
                Directory.CreateDirectory(LogOutputDir);
                string fileName = entry.PlayedAt.ToString("yyyy-MM-dd") + ".json";
                string path = Path.Combine(LogOutputDir, fileName);

                lock (_saveFileLock)
                {
                    List<PlayLogEntry> existing;
                    try
                    {
                        existing = LoadFromFile(path);
                    }
                    catch (JsonException ex)
                    {
                        // 壊れたファイルを「0件だった」ものとして扱い今回の1件だけで
                        // 上書きすると、その日の他の全プレイ履歴が消失してしまう。
                        // 安全のため保存自体を中止する。
                        LogUtils.DebugLogger($"PlayLogRepository.SaveEntry: Aborted save because failed to read {path}: {ex.Message}", true);
                        return;
                    }

                    var idx = existing.FindIndex(e => e.DedupeKey == entry.DedupeKey);

                    if (idx < 0 && oldDedupeKey != null)
                        idx = existing.FindIndex(e => e.DedupeKey == oldDedupeKey);
                    if (idx >= 0)
                        existing[idx] = entry;
                    else
                        existing.Add(entry);

                    existing = [.. existing.OrderBy(e => e.PlayedAt)];
                    string json = JsonSerializer.Serialize(existing, JsonOpts);

                    // クラッシュ・電源断などで書き込み途中にファイルが切り詰められると
                    // 次回の読み込みがJsonExceptionになり得るため、一時ファイルに書いてから
                    // アトミックに置き換える（直接WriteAllTextで上書きしない）
                    string tempPath = path + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogRepository.SaveEntry failed: " + ex.Message, true);
            }
        }

        /// <summary>
        /// 複数のエントリをまとめて保存する。同じ日付(＝同じJSONファイル)に属するエントリは
        /// グループ化し、1グループにつき1回の読み込み・書き込みで済ませる。
        /// SaveEntryをエントリ数分繰り返し呼ぶと、同じ日に何十件もプレイがある場合に
        /// 同一ファイルを何度も読み書きしてしまうため、一括計算(SR/pp再計算等)から使う想定。
        /// 1グループ(1日分)ごとに書き込みが完了するため、処理途中で例外やクラッシュが
        /// 起きても、それまでに完了した日付分は失われない。
        /// </summary>
        public void SaveEntries(IEnumerable<PlayLogEntry> entries)
        {
            foreach (var group in entries.GroupBy(e => e.PlayedAt.ToString("yyyy-MM-dd")))
            {
                SaveEntryGroup(group.Key, [.. group]);
            }
        }

        private void SaveEntryGroup(string fileNameDate, List<PlayLogEntry> entries)
        {
            if (entries.Count == 0) return;
            try
            {
                Directory.CreateDirectory(LogOutputDir);
                string fileName = fileNameDate + ".json";
                string path = Path.Combine(LogOutputDir, fileName);

                lock (_saveFileLock)
                {
                    List<PlayLogEntry> existing;
                    try
                    {
                        existing = LoadFromFile(path);
                    }
                    catch (JsonException ex)
                    {
                        LogUtils.DebugLogger($"PlayLogRepository.SaveEntries: Aborted save because failed to read {path}: {ex.Message}", true);
                        return;
                    }

                    foreach (var entry in entries)
                    {
                        var idx = existing.FindIndex(e => e.DedupeKey == entry.DedupeKey);
                        if (idx >= 0)
                            existing[idx] = entry;
                        else
                            existing.Add(entry);
                    }

                    existing = [.. existing.OrderBy(e => e.PlayedAt)];
                    string json = JsonSerializer.Serialize(existing, JsonOpts);

                    string tempPath = path + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogRepository.SaveEntries failed: " + ex.Message, true);
            }
        }

        public List<PlayLogEntry> LoadAllFromDisk()
        {
            var all = new List<PlayLogEntry>();
            if (!Directory.Exists(LogOutputDir)) return all;

            foreach (var file in Directory.GetFiles(LogOutputDir, "*.json"))
            {
                try
                {
                    all.AddRange(LoadFromFile(file));
                }
                catch (JsonException ex)
                {
                    // 1ファイルの破損で起動時の読み込み全体を失敗させない。
                    // 破損ファイルはスキップし、次回SaveEntryで誤って上書きされないよう
                    // ログに警告を残す（自動修復はしない）。
                    LogUtils.DebugLogger($"PlayLogRepository.LoadAllFromDisk: Skipped because failed to read {file}: {ex.Message}", true);
                }
            }
            return all;
        }

        /// <summary>
        /// ファイルからプレイ履歴を読み込む。JSONが破損している場合は JsonException を
        /// 呼び出し元に伝播させる（「読み込めなかった」と「その日は0件だった」を
        /// 区別できないまま握りつぶすと、SaveEntry側で当日分の履歴を丸ごと上書き消去
        /// してしまう恐れがあるため）。
        /// </summary>
        internal static List<PlayLogEntry> LoadFromFile(string path)
        {
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<List<PlayLogEntry>>(json, JsonOpts) ?? [];
        }
    }
}
