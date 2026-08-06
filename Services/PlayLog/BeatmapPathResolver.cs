using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OsuMate.Models;
using OsuMate.Services.Trainer;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
    public class BeatmapPathResolver
    {
        private readonly OsuMemoryService _memory;

        // md5Map（MD5→BeatmapInfo）を BeatmapID→BeatmapInfo にグルーピングし直したキャッシュする
        // 同じ md5Map インスタンスが渡される限り再構築しないことで、
        // FindBeatmapPathById の呼び出しをO(1)に保つ
        private Dictionary<int, OsuMate.Services.StableDb.BeatmapInfo>? _idMapCache;
        private Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? _idMapCacheSource;

        public BeatmapPathResolver(OsuMemoryService memory)
        {
            _memory = memory;
        }
        public string? FindBeatmapPathByMd5(string? md5, Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map)
        {
            if (string.IsNullOrEmpty(md5) || md5Map == null) return null;
            if (!md5Map.TryGetValue(md5, out var info)) return null;
            if (string.IsNullOrEmpty(info.FolderName) || string.IsNullOrEmpty(info.OsuFileName)) return null;

            var path = Path.Combine(_memory.SongsPath, info.FolderName, info.OsuFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// md5Map（osu!.db から読み込み済みの MD5→BeatmapInfo マップ）を BeatmapID で
        /// グルーピングし直し、O(1)でパスを解決する。Songsフォルダの全件スキャンを避けられるため、
        /// md5Map が利用可能な場合はこちらを優先し、<see cref="FindBeatmapPath"/> はフォールバックとして残す。
        /// </summary>
        public string? FindBeatmapPathById(int beatmapId, Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map)
        {
            if (beatmapId <= 0 || md5Map == null) return null;

            if (_idMapCache == null || !ReferenceEquals(_idMapCacheSource, md5Map))
            {
                var idMap = new Dictionary<int, OsuMate.Services.StableDb.BeatmapInfo>();
                foreach (var info in md5Map.Values)
                {
                    if (info.DifficultyId > 0) idMap[info.DifficultyId] = info;
                }
                _idMapCache = idMap;
                _idMapCacheSource = md5Map;
            }

            if (!_idMapCache.TryGetValue(beatmapId, out var beatmapInfo)) return null;
            if (string.IsNullOrEmpty(beatmapInfo.FolderName) || string.IsNullOrEmpty(beatmapInfo.OsuFileName)) return null;

            var path = Path.Combine(_memory.SongsPath, beatmapInfo.FolderName, beatmapInfo.OsuFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Songsフォルダ全体を1件ずつ開いてBeatmapIDを照合するブルートフォース探索。
        /// 非常に重いため、md5Map が使えない場合の最終フォールバックとしてのみ使うこと。
        /// 通常は <see cref="FindBeatmapPathById"/> を先に試すべき。
        /// </summary>
        public string? FindBeatmapPath(int beatmapId)
        {
            if (!_memory.IsDirectoryLoaded) return null;
            try
            {
                foreach (var dir in Directory.GetDirectories(_memory.SongsPath))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.osu"))
                    {
                        var id = ReadBeatmapIdFromFile(file);
                        if (id == beatmapId) return file;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("BeatmapPathResolver.FindBeatmapPath failed: " + ex.Message, true);
            }
            return null;
        }

        /// <param name="md5Map">
        /// 呼び出し元がosu!.dbから読み込み済みのMD5マップを保持している場合に渡すと、
        /// 未文書化のSongsフォルダ全走査より先にO(1)のID解決を試みる。
        /// </param>
        public string? ResolveBeatmapFilePath(
            OsuMemoryDataProvider.OsuMemoryModels.Direct.CurrentBeatmap beatmap,
            Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map = null)
        {
            try
            {
                if (!_memory.IsDirectoryLoaded || string.IsNullOrWhiteSpace(_memory.SongsPath))
                    return null;

                if (!string.IsNullOrWhiteSpace(beatmap.FolderName) && !string.IsNullOrWhiteSpace(beatmap.OsuFileName))
                {
                    var candidate = Path.Combine(_memory.SongsPath, beatmap.FolderName, beatmap.OsuFileName);
                    if (File.Exists(candidate)) return candidate;
                }

                // Songsフォルダ全体の未文書化な全走査に頼る前に、#2と同じ md5Map ベースの
                // O(1)解決を試みる（呼び出し元がmd5Mapをキャッシュ済みの場合のみ有効）。
                if (beatmap.Id > 0)
                {
                    var byId = FindBeatmapPathById(beatmap.Id, md5Map);
                    if (byId != null) return byId;
                }

                if (!string.IsNullOrWhiteSpace(beatmap.OsuFileName))
                {
                    var fallback = Directory.GetFiles(_memory.SongsPath, "*.osu", SearchOption.AllDirectories)
                        .FirstOrDefault(f => Path.GetFileName(f).Equals(beatmap.OsuFileName, StringComparison.OrdinalIgnoreCase));
                    if (fallback != null) return fallback;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("BeatmapPathResolver.ResolveBeatmapFilePath failed: " + ex.Message, true);
                return null;
            }
        }

        public static string ComputeMd5(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "";
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hash = md5.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("BeatmapPathResolver.ComputeMd5 failed: " + ex.Message, true);
                return "";
            }
        }

        /// <summary>
        /// .osu の [Metadata] セクションから Artist/Title/Version(難易度名)/Creator を読み取る。
        /// 実体は <see cref="OsuBeatmapFile.LoadMetadataOnly"/> に委譲しており、
        /// Trainer側（<see cref="OsuBeatmapFile"/>）と同一のパーサーを共有する。
        /// </summary>
        public static (string artist, string title, string difficulty, string creator) ReadBeatmapMetadataFromFile(string file)
        {
            try
            {
                var bm = OsuBeatmapFile.LoadMetadataOnly(file);
                return (bm.Artist, bm.Title, bm.Version, bm.Creator);
            }
            catch
            {
                return ("", "", "", "");
            }
        }

        /// <summary>
        /// .osu の [Difficulty] CircleSize を mania のキー数として読み取る。
        /// MemoryProvider はルールセットのみを返すため、プレイ開始時にこの値も
        /// スナップショットする。実体は <see cref="OsuBeatmapFile.LoadMetadataOnly"/> に委譲する。
        /// </summary>
        public static int? ReadManiaKeyCountFromFile(string file)
        {
            try
            {
                var bm = OsuBeatmapFile.LoadMetadataOnly(file);
                // CircleSize は未設定時 -1（OsuBeatmapFileの既定値）のまま。見つからなかった場合は null を返す。
                return bm.CircleSize >= 0
                    ? LogModeClassifier.GetManiaKeyCount(3, (double)bm.CircleSize)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// .osu の [Metadata] BeatmapID を読み取る。見つからない場合は -1。
        /// 実体は <see cref="OsuBeatmapFile.LoadMetadataOnly"/> に委譲する。
        /// </summary>
        internal static int ReadBeatmapIdFromFile(string file)
        {
            try
            {
                return OsuBeatmapFile.LoadMetadataOnly(file).BeatmapID;
            }
            catch
            {
                return -1;
            }
        }
    }
}
