using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMemoryDataProvider;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// プレイ履歴をリアルタイムに記録し、日付ごとのJSONファイルへ保存するサービス。
    /// OsuMemoryServiceの状態遷移イベントを購読し、完走・中断プレイのセッション管理をオーケストレートする。
    /// JSON保存・譜面パス解決・SR/pp計算・過去データ取り込みなどの専門処理は各専用クラスへ委譲する。
    /// </summary>
    public class PlayLogService
    {
        private readonly OsuMemoryService _memory;
        private readonly PpCalculationService _ppService;
        private readonly Dispatcher _dispatcher;
        private readonly PlayLogRepository _repository;
        private readonly BeatmapPathResolver _pathResolver;
        private readonly PlayLogSrPpEnricher _srPpEnricher;
        private readonly HistoricalImporter _historicalImporter;

        private bool _isRealtimeTrackingEnabled = true;

        // プレイ開始時のスナップショット（メモリ読み取りベースの一時保存）
        private PlaySessionSnapshot? _currentSession;

        // UIへ公開するリスト（逆時系列: 新しいものが先頭）
        public ObservableCollection<PlayLogEntry> Entries { get; } = [];

        // DedupeKey → PlayLogEntry。重複防止に加え、scores.db 突き合わせ時に
        // 仮登録済みエントリを O(1) で探し当てるためにも使う（複数スレッドから触るため ConcurrentDictionary）。
        private readonly ConcurrentDictionary<string, PlayLogEntry> _entriesByKey = new();

        // 起動時に一度だけ読んだ osu!.db の MD5→BeatmapInfo キャッシュ
        private Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? _md5Map;

        // scores.db のパス（起動時一括取り込みで設定）
        private string? _scoresDbPath;

        public PlayLogService(
            OsuMemoryService memory,
            PpCalculationService ppService,
            Dispatcher dispatcher,
            PlayLogRepository repository,
            BeatmapPathResolver pathResolver,
            PlayLogSrPpEnricher srPpEnricher,
            HistoricalImporter historicalImporter)
        {
            _memory              = memory;
            _ppService           = ppService;
            _dispatcher          = dispatcher;
            _repository          = repository;
            _pathResolver        = pathResolver;
            _srPpEnricher        = srPpEnricher;
            _historicalImporter  = historicalImporter;

            // 状態遷移イベントを購読
            _memory.OnStatusChanged += HandleStatusChanged;

            // 毎ポーリングTickを購読（SongSelectを経由しないクイックリトライの検知用）
            _memory.OnMemoryRead += HandleMemoryTick;

            // osu! ディレクトリが確定したら DB を再読み込みする
            _memory.OnOsuDirectoryLoaded += HandleOsuDirectoryLoaded;
        }

        // ─── osu!ディレクトリ確定時の再読み込み ─────────────────────────────────────

        /// <summary>
        /// <see cref="OsuMemoryService.OnOsuDirectoryLoaded"/> のハンドラ本体。
        /// ここは fire-and-forget（await されない）呼び出しになるため、内部で例外を捕まえずに投げると
        /// 「Unobserved task exception」としてファイナライザスレッドで再スローされてしまう。
        /// 実処理は <see cref="HandleOsuDirectoryLoadedAsync"/> に任せ、ここでは
        /// 必ず try/catch でログに残してアプリ全体をクラッシュさせないようにする。
        /// </summary>
        private void HandleOsuDirectoryLoaded(string osuDir)
        {
            var unused = Task.Run(async () =>
            {
                try
                {
                    await HandleOsuDirectoryLoadedAsync();
                }
                catch (Exception ex)
                {
                    LogUtils.DebugLogger("PlayLogService.OnOsuDirectoryLoaded: Exception occurred during processing: " + ex, true);
                }
            });
        }

        /// <summary>
        /// osu!.db / scores.db を再取り込みし、既存の <see cref="Entries"/> に対して
        /// 未取り込み分だけを差分マージする（起動時の <see cref="LoadAndCalculateAsync"/> と並行して
        /// 呼ばれる可能性があるため、Entries を空にせず追記のみ行う）。
        /// </summary>
        private async Task HandleOsuDirectoryLoadedAsync()
        {
            var newEntries = _historicalImporter.LoadFromLocalOsuData(out var md5Map, out var scoresDbPath);
            // キャッシュをフィールドに反映（まだ未設定の場合のみ上書き）
            _md5Map       ??= md5Map;
            _scoresDbPath ??= scoresDbPath;

            // 初期ロードと並行してこのイベントが来る場合がある。
            // Entries はまだ空でも、JSONには計算済みの同一キーが存在し得るため、
            // scores.db由来のSR/pp=nullエントリで上書きしてはならない。
            var persistedEntries = _repository.LoadAllFromDisk();
            var persistedByKey = SelectBestByDedupeKey(persistedEntries);

            var addedEntries = new List<PlayLogEntry>();

            await _dispatcher.InvokeAsync(() =>
            {
                // Entries は PlayedAt 降順を維持したまま挿入する必要があり、二分探索で挿入位置を求める。
                void InsertSorted(PlayLogEntry item)
                {
                    int index = FindInsertIndexDescending(Entries, item.PlayedAt);
                    Entries.Insert(index, item);
                }

                // 初期ロードがDB準備前に空振りしていても、JSONの計算済み行を
                // ここで必ず画面へ復元する。
                foreach (var persisted in persistedByKey.Values.OrderByDescending(e => e.PlayedAt))
                {
                    if (_entriesByKey.TryAdd(persisted.DedupeKey, persisted))
                        InsertSorted(persisted);
                }

                foreach (var entry in newEntries.OrderByDescending(e => e.PlayedAt))
                {
                    if (persistedByKey.ContainsKey(entry.DedupeKey) ||
                        !_entriesByKey.TryAdd(entry.DedupeKey, entry)) continue;
                    InsertSorted(entry);
                    addedEntries.Add(entry);
                }
            });

            // 新規追加分はここで JSON に保存
            foreach (var entry in addedEntries)
                _repository.SaveEntry(entry);

            // SR/pp 未計算エントリをバックグラウンドで計算
            await _srPpEnricher.CalculateMissingSrPpAsync(_dispatcher, Entries, _md5Map);
        }

        /// <summary>
        /// エントリ集合を DedupeKey ごとにグループ化し、各グループから最も「完成度の高い」ものを選ぶ。
        /// 優先順位: ① SR/pp計算済み ＞ 未計算　② 計算失敗フラグが立っていない ＞ 立っている　③ PlayedAt が新しい。
        /// <see cref="LoadAndCalculateAsync"/> と <see cref="HandleOsuDirectoryLoadedAsync"/> の双方で
        /// 同一の選定ロジックが必要なため、ここに共通化する。
        /// </summary>
        private static Dictionary<string, PlayLogEntry> SelectBestByDedupeKey(IEnumerable<PlayLogEntry> entries)
        {
            return entries
                .GroupBy(e => e.DedupeKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(e => e.StarRating.HasValue || e.Pp.HasValue ? 1 : 0)
                          .ThenByDescending(e => e.IsCalculationFailed ? 1 : 0)
                          .ThenByDescending(e => e.PlayedAt)
                          .First());
        }

        // ─── 起動時処理 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 起動時に過去のログを読み込み、SR/pp 未計算のエントリを計算してUIに反映する。
        /// バックグラウンドスレッドで実行
        /// </summary>
        public async Task LoadAndCalculateAsync()
        {
            try
            {
                var allEntries = _repository.LoadAllFromDisk();
                LogUtils.DebugLogger($"PlayLogService: LoadAllFromDisk={allEntries.Count}", true);

                var historicalEntries = _historicalImporter.LoadFromLocalOsuData(out var md5Map, out var scoresDbPath);
                // md5Map と scoresDbPath をフィールドにキャッシュ（ResultsScreen 差分取り込みで再利用）
                _md5Map       = md5Map;
                _scoresDbPath = scoresDbPath;
                LogUtils.DebugLogger($"PlayLogService: LoadHistorical={historicalEntries.Count}", true);

                // 重複チェックを通して新たに追加されるプレイ(JSONに存在しないもの)を特定し、先に保存しておく
                // scores.db にはSR/ppも計算失敗フラグもないため、同一キーでは
                // 並び順に依存せず、永続化済みのJSONエントリを必ず残す。
                var savedByKey = SelectBestByDedupeKey(allEntries);

                var combinedByKey = new Dictionary<string, PlayLogEntry>(savedByKey);
                var entriesToSave = new List<PlayLogEntry>();

                foreach (var historical in historicalEntries)
                {
                    if (combinedByKey.TryGetValue(historical.DedupeKey, out var persisted))
                    {
                        // 新設したモード情報だけはDBから補完する。計算結果は上書きしない。
                        if (ApplyHistoricalModeMetadata(persisted, historical))
                            entriesToSave.Add(persisted);
                        continue;
                    }

                    combinedByKey[historical.DedupeKey] = historical;
                    entriesToSave.Add(historical);
                }

                foreach (var entry in entriesToSave)
                    _repository.SaveEntry(entry);

                var combined = combinedByKey.Values
                    .GroupBy(e => e.DedupeKey)
                    .Select(g => g
                        // SR/pp 計算済み（JSON由来）のエントリを優先する。
                        // scores.db 由来のエントリは常に StarRating=null なので、
                        // 計算済みの値が失われないようにする。
                        .OrderByDescending(e => e.StarRating.HasValue ? 1 : 0)
                        .ThenByDescending(e => e.PlayedAt)
                        .First())
                    .ToList();

                // 新しい順に並べ替えて公開
                var sorted = combined.OrderByDescending(e => e.PlayedAt).ToList();

                await _dispatcher.InvokeAsync(() =>
                {
                    Entries.Clear();
                    _entriesByKey.Clear();
                    foreach (var entry in sorted)
                    {
                        Entries.Add(entry);
                        _entriesByKey[entry.DedupeKey] = entry;
                    }
                });

                // SR/pp 未計算エントリをバックグラウンドで計算
                await _srPpEnricher.CalculateMissingSrPpAsync(_dispatcher, Entries, _md5Map);


            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogService.LoadAndCalculateAsync failed: " + ex.Message, true);
            }
        }

        // ─── リアルタイムキャプチャ ─────────────────────────────────────────────────

        /// <summary>
        /// PlayedAt降順に並んだリストに対し、指定した時刻を挿入すべき位置を二分探索で求める
        /// </summary>
        private static int FindInsertIndexDescending(IList<PlayLogEntry> list, DateTime playedAt)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (list[mid].PlayedAt > playedAt) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static bool ApplyHistoricalModeMetadata(PlayLogEntry persisted, PlayLogEntry historical)
        {
            bool changed = false;

            if (persisted.Mode != historical.Mode)
            {
                persisted.Mode = historical.Mode;
                changed = true;
            }

            if (persisted.ManiaKeyCount != historical.ManiaKeyCount)
            {
                persisted.ManiaKeyCount = historical.ManiaKeyCount;
                changed = true;
            }

            return changed;
        }

        private void HandleStatusChanged(OsuMemoryStatus prev, OsuMemoryStatus current)
        {
            try
            {
                if (!_isRealtimeTrackingEnabled) return;

                // 実ソロプレイの開始判定:
                // ・SongSelect からの遷移のみを対象にする（マルチプレイは MultiplayerRoom 系を経由するため自然に除外される、
                //   またアプリ起動直後に既に Playing だった場合＝prev=Unknown も除外できる）
                // ・Player.IsReplay でリプレイ観戦を除外する
                if (current == OsuMemoryStatus.Playing
                    && prev == OsuMemoryStatus.SongSelect
                    && _memory.IsOsuRunning
                    && _memory.IsDirectoryLoaded
                    && !_memory.GetBaseAddressSnapshot().Player.IsReplay)
                {
                    CaptureSessionStart();
                    return;
                }

                // プレイ終了の検知（中断 or リザルト画面）
                if (prev == OsuMemoryStatus.Playing && _currentSession != null)
                {
                    bool isCompleted = current == OsuMemoryStatus.ResultsScreen;
                    CommitSession(isCompleted, current);
                }

                // リザルト画面からの遷移: 既存エントリを完了データで更新
                if (prev == OsuMemoryStatus.ResultsScreen && _currentSession != null)
                {
                    UpdateLastEntryAsCompleted();
                    _currentSession = null;
                }
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogService.HandleStatusChanged failed: " + ex.Message, true);
            }
        }

        /// <summary>
        /// 毎ポーリングTickで呼ばれる。osu!のクイックリトライは SongSelect を経由せず
        /// OsuStatus が Playing のまま即座に再スタートするため、OnStatusChanged だけでは検知できない。
        /// GeneralData.Retries の増加を監視し、増えた瞬間に「直前セッションを中断確定 → 新セッション開始」を行う。
        /// </summary>
        private void HandleMemoryTick()
        {
            try
            {
                if (!_isRealtimeTrackingEnabled) return;
                if (_currentSession == null) return;
                // ResultsScreen 表示中などは対象外（Playing中のみ監視する）
                if (_memory.CurrentStatus != OsuMemoryStatus.Playing) return;

                // Playing 中の毎tickで「最後に読めた正常値」をスナップショットにキャッシュしておく。
                // 状態遷移(Playing→SongSelect等)の直後に Player を直接読むと、既に値が使い回されて
                // 壊れている(例: 判定数が数万になる)ことがあるため、中断コミット時はこちらを使う。
                var livePlayer = _memory.GetBaseAddressSnapshot().Player;
                RefreshSessionMetadataFromLivePlayer();
                _currentSession.LastHit300   = livePlayer.Hit300;
                _currentSession.LastHit100   = livePlayer.Hit100;
                _currentSession.LastHit50    = livePlayer.Hit50;
                _currentSession.LastHitGeki  = livePlayer.HitGeki;
                _currentSession.LastHitKatu  = livePlayer.HitKatu;
                _currentSession.LastHitMiss  = livePlayer.HitMiss;
                _currentSession.LastMaxCombo = livePlayer.MaxCombo;
                _currentSession.LastScore    = livePlayer.Score;
                _currentSession.LastAccuracy = livePlayer.Accuracy;

                int currentRetries = _memory.GetBaseAddressSnapshot().GeneralData.Retries;
                if (currentRetries == _currentSession.StartRetries) return;

                // 状態遷移なしのクイックリトライを検知: 直前のセッションを中断として確定し、新セッションを開始する
                CommitSession(isCompleted: false, endStatus: OsuMemoryStatus.Playing);
                if (!_memory.GetBaseAddressSnapshot().Player.IsReplay)
                    CaptureSessionStart();
            }
            catch (Exception ex)
            {
                LogUtils.DebugLogger("PlayLogService.HandleMemoryTick failed: " + ex.Message, true);
            }
        }

        /// <summary>プレイ開始時の状態をスナップショットとして保存する。</summary>
        private void RefreshSessionMetadataFromLivePlayer()
        {
            if (_currentSession == null) return;

            // SongSelect -> Playing の遷移直後は、Player の前プレイ値がまだ残ることがある。
            // Playing 中に繰り返し上書きすることで、コミット時には安定した現プレイ値を使う。
            int mode = _memory.CurrentOsuGamemode;
            if (mode is >= 0 and <= 3)
            {
                _currentSession.Mode = mode;

                if (mode != 3)
                {
                    _currentSession.ManiaKeyCount = null;
                }
                else if (_currentSession.ManiaKeyCount == null)
                {
                    var beatmapPath = _pathResolver.ResolveBeatmapFilePath(_memory.GetBaseAddressSnapshot().Beatmap, _md5Map);
                    if (beatmapPath != null)
                        _currentSession.ManiaKeyCount = BeatmapPathResolver.ReadManiaKeyCountFromFile(beatmapPath);
                }
            }

            var playerName = _memory.GetBaseAddressSnapshot().Player.Username;
            if (!string.IsNullOrWhiteSpace(playerName))
                _currentSession.PlayerName = playerName;
        }

        private void CaptureSessionStart()
        {
            var baseAddresses = _memory.GetBaseAddressSnapshot();

            // Autoplay (2048) を含んでいる場合は記録しない
            if ((baseAddresses.GeneralData.Mods & 2048) == 2048) return;

            var bm       = baseAddresses.Beatmap;
            var filePath = _pathResolver.ResolveBeatmapFilePath(bm, _md5Map);
            var (artist, title, difficulty, creator) = filePath != null
                ? BeatmapPathResolver.ReadBeatmapMetadataFromFile(filePath)
                : ("", "", "", "");
            int mode = _memory.CurrentOsuGamemode;
            int? maniaKeyCount = mode == 3 && filePath != null
                ? BeatmapPathResolver.ReadManiaKeyCountFromFile(filePath)
                : null;

            string playerName = baseAddresses.Player.Username ?? "";
            _currentSession = new PlaySessionSnapshot
            {
                StartedAt       = DateTime.Now,
                BeatmapId       = bm.Id,
                BeatmapSetId    = bm.SetId,
                Artist          = artist,
                Title           = title,
                DifficultyName  = difficulty,
                Creator         = creator,
                FolderName      = bm.FolderName ?? "",
                OsuFileName     = bm.OsuFileName ?? "",
                BeatmapMd5      = BeatmapPathResolver.ComputeMd5(filePath),
                PlayerName      = playerName,
                Mode            = mode,
                ManiaKeyCount   = maniaKeyCount,
                Mods            = _ppService.PrevMods,
                ModsRaw         = baseAddresses.GeneralData.Mods,
                OverallDifficulty = bm.Od,
                StartRetries    = baseAddresses.GeneralData.Retries,
            };
        }

        // ─── セッション確定 ─────────────────────────────────────────────────────────

        /// <summary>プレイ終了時に PlayLogEntry を作成してログに追加する。</summary>
        private void CommitSession(bool isCompleted, OsuMemoryStatus endStatus)
        {
            if (_currentSession == null) return;
            // 注意: ここでは _memory.CurrentStatus はもう Playing ではない(状態遷移経由で
            // 呼ばれる場合は既に ResultsScreen/SongSelect に変わった後)。
            // 「実プレイかどうか」は CaptureSessionStart の時点(prev==SongSelect && !IsReplay、
            // またはリトライ再開時の !IsReplay)で既に確定させているので、ここで CurrentStatus==Playing
            // を再チェックすると常に false になってしまい、コミットが一切走らなくなる。
            if (!_memory.IsOsuRunning)
            {
                _currentSession = null;
                return;
            }

            var snap             = _currentSession;
            var isCompletedNow   = endStatus == OsuMemoryStatus.ResultsScreen;
            var displayMods      = OsuUtils.ParseMods(snap.ModsRaw).Display;
            var modsStr          = displayMods.Length == 0 ? "NM" : string.Join(",", displayMods.Select(m => m.ToUpper()));

            // 判定内訳: 完走時は後続の UpdateLastEntryAsCompleted (ResultsScreen 離脱時) で正確な値に上書きされる。
            // 中断時は Playing 中の最後の正常値 (snap.Last*) が最終値となる。

            // InGameOverlay 向けに計算済みの最新SR/ppがあれば取得
            // pp は最後まで(ResultsScreenまで)プレイしきったスコアに対してのみ意味を持つ値のため、
            // 中断プレイ(isCompleted=false)では採用せず null のままにする。SRは譜面自体の指標なので中断でも設定する。
            var lastData  = _ppService.LastCalculatedData;
            double? initialSr = lastData?.DifficultyAttributes?.StarRating;
            double? initialPp = isCompleted ? lastData?.CurrentPerformanceAttributes?.Total : null;

            var entry = new PlayLogEntry
            {
                // 完走: ResultsScreen 遷移時刻（≒スコア確定時刻）、中断: Playing 離脱時刻
                PlayedAt         = DateTime.Now,
                BeatmapId        = snap.BeatmapId,
                BeatmapSetId     = snap.BeatmapSetId,
                Artist           = snap.Artist,
                Title            = snap.Title,
                DifficultyName   = snap.DifficultyName,
                Creator          = snap.Creator,
                PlayerName       = snap.PlayerName,
                Mode             = snap.Mode,
                ManiaKeyCount    = snap.ManiaKeyCount,
                OverallDifficulty = snap.OverallDifficulty,
                ModsString       = modsStr,
                IsCompleted      = isCompleted,
                Count300         = snap.LastHit300,
                Count100         = snap.LastHit100,
                Count50          = snap.LastHit50,
                CountGeki        = snap.LastHitGeki,
                CountKatu        = snap.LastHitKatu,
                CountMiss        = snap.LastHitMiss,
                MaxCombo         = snap.LastMaxCombo,
                TotalScore       = snap.LastScore,
                Accuracy         = snap.LastAccuracy,
                BeatmapMd5       = snap.BeatmapMd5,
                ModsRaw          = snap.ModsRaw,
                // 完走プレイはメモリ由来で確定済み（scores.db の確認不要）
                // 中断プレイは scores.db に対応物が存在しないため暫定扱いのまま
                IsProvisional    = !isCompleted,
                StarRating       = initialSr,
                Pp               = initialPp,
            };

            // DedupeKey:
            // ・完走プレイ(isCompleted=true) → scores.db と共通の結合キー(BeatmapMd5+PlayerName+ModsRaw+TotalScore)。
            //   時刻を使わないのは、scores.db にも同じキー式を使って起動時一括取り込みで重複を除けるようにするため。
            // ・中断プレイ → scores.db に対応物が来ないので、終了時刻ベースのキー。
            entry.DedupeKey = isCompleted
                ? PlayLogKeyBuilder.MakeCompletedJoinKey(entry.BeatmapMd5, entry.PlayerName, entry.ModsRaw, entry.TotalScore)
                : PlayLogKeyBuilder.MakeInterruptedKey(entry);

            if (!_entriesByKey.TryAdd(entry.DedupeKey, entry)) return;

            // ResultsScreen 離脱時の UpdateLastEntryAsCompleted が同じエントリを取り違えなく探せるようにしておく
            if (isCompleted)
                snap.PendingCompletedKey = entry.DedupeKey;

            // UIリストの先頭に追加（逆時系列）
            _dispatcher.InvokeAsync(() => Entries.Insert(0, entry));

            // バックグラウンドで JSON に保存
            _ = Task.Run(() => _repository.SaveEntry(entry));

            // セッションを保持（ResultsScreen 上書き用）
            if (!isCompleted)
                _currentSession = null;
            // isCompleted = true の場合は _currentSession を残して ResultsScreen 遷移後に上書き更新
        }

        /// <summary>ResultsScreen から抜けたとき、最後のエントリを確定データで更新する。</summary>
        private void UpdateLastEntryAsCompleted()
        {
            if (_currentSession == null) return;

            var oldKey = _currentSession.PendingCompletedKey;
            if (oldKey == null || !_entriesByKey.TryGetValue(oldKey, out var existing))
            {
                _currentSession = null;
                return;
            }

            RefreshSessionMetadataFromLivePlayer();
            var rs = _memory.GetBaseAddressSnapshot().ResultsScreen;

            // existing は Entries（Logタブ表示中は LogGrid.ItemsSource にバインド済み）の要素なので、
            // プロパティ更新は必ずUIスレッドで行う。バックグラウンドスレッドから直接書き換えると
            // バインド経由の InvalidOperationException で以降の処理（IsCompleted確定・保存）が
            // 中断され、そのプレイの記録が欠落する。
            string newKey = null!;
            string? staleKey = null;
            _dispatcher.Invoke(() =>
            {
                // 完走時は結果画面のモードで最終補正する。
                existing.Mode          = rs.Mode;
                existing.ManiaKeyCount = existing.Mode == 3
                    ? _currentSession.ManiaKeyCount
                    : null;
                existing.Count300    = rs.Hit300;
                existing.Count100    = rs.Hit100;
                existing.Count50     = rs.Hit50;
                existing.CountGeki   = rs.HitGeki;
                existing.CountKatu   = rs.HitKatu;
                existing.CountMiss   = rs.HitMiss;
                existing.MaxCombo    = rs.MaxCombo;
                existing.TotalScore  = rs.Score;
                existing.IsCompleted = true;

                // ResultsScreen 表示中にスコアが補正されている可能性があるため結合キーを再計算する。
                // scores.db 側もこれと同じ式でキーを作って探しに来るので、ここでズレたままにしておくと突き合わせに失敗する。
                newKey = PlayLogKeyBuilder.MakeCompletedJoinKey(existing.BeatmapMd5, existing.PlayerName, existing.ModsRaw, existing.TotalScore);
                if (newKey != oldKey)
                {
                    staleKey = oldKey;
                    existing.DedupeKey = newKey;
                }
            });

            // _entriesByKey（ConcurrentDictionary）の操作自体はスレッドセーフなので、
            // ディスパッチの範囲に含めずバックグラウンドのままでよい。
            if (staleKey != null)
                _entriesByKey.TryRemove(staleKey, out _);
            _entriesByKey[newKey] = existing;

            _ = Task.Run(() => _repository.SaveEntry(existing, staleKey));
            _currentSession = null;
        }
    }
}
