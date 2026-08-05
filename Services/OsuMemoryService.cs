using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using System.IO;

namespace OsuMate.Services
{
    public class OsuMemoryService
    {
        private readonly StructuredOsuMemoryReader _sreader = StructuredOsuMemoryReader.GetInstance(new("osu!"));

        // osu!プロセスのメモリから読み取った生データ。StartMemoryReader（単一のライタースレッド）のみが
        // 書き換える。外部への公開はGetBaseAddressSnapshot()／ReadPlayerHitErrors()等の専用メソッド経由に
        // 限定し、ロックの取り忘れ・誤ったロックオブジェクトの使用を防ぐ。
        private readonly OsuBaseAddresses _baseAddresses = new();

        public event Action? OnMemoryRead;
        public event Action<IntPtr>? OnOsuWindowFound;

        /// <summary>OsuMemoryStatus が変化したときに発火。引数は (前回, 今回)。</summary>
        public event Action<OsuMemoryStatus, OsuMemoryStatus>? OnStatusChanged;

        /// <summary>osu! のディレクトリが初めて確定したときに発火。</summary>
        public event Action<string>? OnOsuDirectoryLoaded;

        internal bool IsOsuRunning { get; private set; }
        internal bool IsDirectoryLoaded { get; private set; }
        internal bool IsPlaying { get; private set; }
        internal bool IsResultScreen { get; private set; }
        internal int CurrentOsuGamemode { get; private set; }
        internal OsuMemoryStatus CurrentStatus { get; private set; }
        private OsuMemoryStatus _prevStatus = OsuMemoryStatus.Unknown;
        internal string OsuDirectory { get; private set; } = string.Empty;
        internal string SongsPath { get; private set; } = string.Empty;

        /// <summary>
        /// osu!ディレクトリ(空文字ならプロセスからの自動検出)
        /// 履歴データの閲覧・取り込みのために使用
        /// </summary>
        internal string ManualOsuDirectory { get; set; } = string.Empty;

        private readonly List<(double timeSec, double offsetMs)> _urTimelineData = [];
        internal string[] PrevMods { get; set; } = [];
        internal double FirstObjectTimeModified { get; set; } = 0;

        // UR タイムライン（_urTimelineData）の読み書き排他制御用。外部には公開せず、
        // GetURTimelineSnapshot() / SyncURTimeline() 経由でのみ触らせる。
        private readonly object _urTimelineLock = new();

        // Player（HitErrors含む）の読み書き排他制御用。
        // StartMemoryReader が TryRead で _baseAddresses.Player を書き換えるが、
        // PpCalculationService / MainViewModel が別スレッドから HitErrors 等を読むため、
        // 両者で同じロックを共有して同時アクセスを防ぐ。
        // ReadPlayerHitErrors() / ReadHitsAndAccuracy() /
        // TryCopyPlayerHitErrors() の専用メソッド経由でのみ使わせる。
        private readonly object _playerLock = new();
        private int _previousHitCount = 0;

        // StartProcessMonitor が取得したパスをキャッシュし、
        // StartMemoryReader で再利用することで GetOsuProcess の二重呼び出しを防ぐ
        private string _cachedOsuPath = string.Empty;
        private IntPtr _lastMainWindowHandle = IntPtr.Zero;

        // osu mateを先に起動した場合、OsuMemoryDataProviderのシグネチャスキャンが
        // osu!の初期化完了前に誤ヒットし、誤ったアドレスを永久にキャッシュする不具合への対策。
        // これを防ぐため、osu!プロセス検知後、メインウィンドウ確認から一定時間経過するまで
        // TryReadの呼び出し(＝初回スキャン)を意図的に遅延させる。
        private const int MemoryReadWarmupMs = 6000;
        private int _lastSeenOsuPid = -1;
        private DateTime? _osuWindowSeenAt = null;
        private volatile bool _isMemoryReadReady = false;

        /// <summary>
        /// 現在アタッチ中のosu!プロセスに対して、TryRead(＝ライブラリ内部の固定アドレス
        /// キャッシュの確定)を開始してよい状態かどうか。false の間はStartMemoryReaderが
        /// 一切のTryReadを呼ばないため、シグネチャスキャンの誤ヒットが発生し得ない。
        /// </summary>
        internal bool IsMemoryReadReady => _isMemoryReadReady;

        /// <summary>
        /// osu!プロセスから読み取った生データの直接参照を返す。
        /// スナップショットの複製ではないため、Player配下(HitErrors等)を読む際は競合のリスクがある。
        /// Player配下へアクセスする場合は、本メソッドではなく <see cref="ReadPlayerHitErrors"/> 等の専用メソッドを使用する。
        /// </summary>
        internal OsuBaseAddresses GetBaseAddressSnapshot() => _baseAddresses;

        // HitErrorsが1000ms超える値は、
        // torn read（ガベージバイト列をそのままintとして解釈してしまう現象）による破綻値とみなし、
        // 直前の安全なスナップショットへフォールバックする。
        private const int MaxPlausibleHitErrorMs = 1000;

        private static bool IsPlausibleHitErrors(List<int> hitErrors)
        {
            foreach (int v in hitErrors)
            {
                if (v > MaxPlausibleHitErrorMs || v < -MaxPlausibleHitErrorMs)
                    return false;
            }
            return true;
        }

        private List<int> _lastSafeHitErrorsFast = [];

        /// <summary>
        /// PlayerLock配下でHitErrorsを安全にコピーして返す。
        /// torn read等で破綻した値を検出した場合は、直前に取得できていた安全なスナップショットを返す。
        /// </summary>
        internal List<int> ReadPlayerHitErrors()
        {
            lock (_playerLock)
            {
                if (_baseAddresses.Player.HitErrors is not { } src) return [];

                List<int> copy;
                try
                {
                    copy = [.. src];
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    return _lastSafeHitErrorsFast;
                }

                if (!IsPlausibleHitErrors(copy))
                    return _lastSafeHitErrorsFast;

                return _lastSafeHitErrorsFast = copy;
            }
        }

        /// <summary>
        /// PlayerLock配下でHitErrorsの安全なコピーを試みる。成功すれば(false, Copy)、
        /// 失敗した場合（sourceがnull、torn readによる例外、または明らかに破綻した値を検出した場合）は
        /// (false, null) もしくは (true, null) を返し、呼び出し元は直前のスナップショットを使用する。
        /// </summary>
        internal (bool SourceWasNull, List<int>? Copy) TryCopyPlayerHitErrors()
        {
            lock (_playerLock)
            {
                var hitErrors = _baseAddresses.Player.HitErrors;
                if (hitErrors == null) return (true, null);
                try
                {
                    List<int> copy = [.. hitErrors];
                    return IsPlausibleHitErrors(copy) ? (false, copy) : (false, null);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    return (false, null);
                }
            }
        }

        // 判定数・コンボの妥当性チェック用の上限。現実的なマラソン譜面でも1万強程度に収まるため、
        // 十分な余裕を見た値にしている。Scoreはmod倍率次第で数千万に達し得るため上限は設けず、
        // 負値のみを異常とみなす。
        private const int MaxPlausibleJudgementCount = 20000;
        private const int MaxPlausibleCombo = 20000;

        private static bool IsPlausibleHits(HitsResult hits, double accuracy)
        {
            if (hits.HitGeki < 0 || hits.HitGeki > MaxPlausibleJudgementCount) return false;
            if (hits.Hit300 < 0 || hits.Hit300 > MaxPlausibleJudgementCount) return false;
            if (hits.HitKatu < 0 || hits.HitKatu > MaxPlausibleJudgementCount) return false;
            if (hits.Hit100 < 0 || hits.Hit100 > MaxPlausibleJudgementCount) return false;
            if (hits.Hit50 < 0 || hits.Hit50 > MaxPlausibleJudgementCount) return false;
            if (hits.HitMiss < 0 || hits.HitMiss > MaxPlausibleJudgementCount) return false;
            if (hits.Combo < 0 || hits.Combo > MaxPlausibleCombo) return false;
            if (hits.Score < 0) return false;
            if (double.IsNaN(accuracy) || accuracy < 0 || accuracy > 100.5) return false;
            return true;
        }

        private HitsResult _lastSafeHits = new();
        private double _lastSafeAccuracy;

        /// <summary>
        /// PlayerLock配下で、現在の HitsResult（判定数等）と Accuracy をまとめて読み取る。
        /// </summary>
        internal (HitsResult Hits, double Accuracy) ReadHitsAndAccuracy()
        {
            lock (_playerLock)
            {
                var hits = new HitsResult();
                hits.SetValueFromMemory(CurrentStatus, _baseAddresses, IsPlaying);
                double accuracy = _baseAddresses.Player.Accuracy;

                if (!IsPlausibleHits(hits, accuracy))
                    return (_lastSafeHits.Clone(), _lastSafeAccuracy);

                _lastSafeHits = hits;
                _lastSafeAccuracy = accuracy;
                return (hits, accuracy);
            }
        }

        /// <summary>
        /// UR タイムライン（<see cref="SyncURTimeline"/>で更新される蓄積データ）のスナップショットを
        /// コピーして返す。呼び出し元がロックオブジェクトを意識せずに済むようにする。
        /// </summary>
        internal List<(double timeSec, double offsetMs)> GetURTimelineSnapshot()
        {
            lock (_urTimelineLock)
            {
                return [.. _urTimelineData];
            }
        }

        /// <summary>
        /// URタイムラインを最新のHitErrors差分から更新する。
        /// リトライ等によるHitErrors減少時は全体を再構築し、増加時は差分を追記する。
        /// </summary>
        internal void SyncURTimeline(IReadOnlyList<int> hitErrors, double speedMultiplier)
        {
            int currentCount = hitErrors.Count;
            double audioTime = _baseAddresses.GeneralData.AudioTime;

            lock (_urTimelineLock)
            {
                if (currentCount > _previousHitCount)
                {
                    for (int i = _previousHitCount; i < currentCount; i++)
                    {
                        double timeSec = (audioTime * speedMultiplier - FirstObjectTimeModified) / 1000.0;
                        _urTimelineData.Add((timeSec, hitErrors[i] * speedMultiplier));
                    }
                }
                else if (currentCount < _previousHitCount)
                {
                    _urTimelineData.Clear();
                    for (int i = 0; i < currentCount; i++)
                    {
                        _urTimelineData.Add((0, hitErrors[i] * speedMultiplier));
                    }
                }
                _previousHitCount = currentCount;
            }
        }

        internal void StartProcessMonitor(CancellationToken ct = default)
        {
            new Thread(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(1000);
                        var (running, path, handle, pid) = ProcessUtils.GetOsuProcess();
                        IsOsuRunning = running;

                        if (!running)
                        {
                            _lastMainWindowHandle = IntPtr.Zero;
                            // osu!が終了したとき、次に見つかるのは必ず新しいプロセスなので、
                            // ウォームアップ状態をリセットする。
                            _lastSeenOsuPid = -1;
                            _osuWindowSeenAt = null;
                            _isMemoryReadReady = false;
                        }
                        else
                        {
                            // PIDが変わった(初回検出、またはosu!自身の再起動)場合は、
                            // このプロセスに対してまだ一度もアドレス解決していないとみなし、
                            // ウォームアップをやり直す。
                            if (pid != _lastSeenOsuPid)
                            {
                                _lastSeenOsuPid = pid;
                                _osuWindowSeenAt = null;
                                _isMemoryReadReady = false;
                            }

                            if (handle != IntPtr.Zero)
                            {
                                if (handle != _lastMainWindowHandle)
                                {
                                    _lastMainWindowHandle = handle;
                                    OnOsuWindowFound?.Invoke(handle);
                                }

                                // メインウィンドウが確認できた時点を猶予時間の起点にする
                                _osuWindowSeenAt ??= DateTime.UtcNow;
                            }

                            if (!_isMemoryReadReady && _osuWindowSeenAt.HasValue &&
                                (DateTime.UtcNow - _osuWindowSeenAt.Value).TotalMilliseconds >= MemoryReadWarmupMs)
                            {
                                _isMemoryReadReady = true;
                            }
                        }

                        // パスが取れたらキャッシュしておく
                        if (!string.IsNullOrEmpty(path))
                            _cachedOsuPath = path;
                    }
                    catch (Exception e) { LogUtils.DebugLogger(e.Message, true); }
                }
            })
            { IsBackground = true }.Start();
        }

        /// <summary>
        /// メモリ読み取りループの間隔(ms)を毎tick取得するためのデリゲート。
        /// GlobalConfig.DataUpdateIntervalMs（SettingsViewから変更可能）を参照する想定。
        /// 呼び出し元が省略した場合は既定の15msで動作する。
        /// </summary>
        internal void StartMemoryReader(Func<int>? intervalMsProvider = null, CancellationToken ct = default)
        {
            var getIntervalMs = intervalMsProvider ?? (() => 15);

            new Thread(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(Math.Max(1, getIntervalMs()));

                        // ディレクトリの確定は osu! の起動状態に関わらず試みる。
                        // （osu!が起動していなくても、プレイ履歴の閲覧・取り込みのためにSongsPathを確定させたいため）
                        if (!IsDirectoryLoaded)
                        {
                            string? resolvedDir = null;

                            if (!string.IsNullOrWhiteSpace(ManualOsuDirectory) && Directory.Exists(ManualOsuDirectory))
                                resolvedDir = ManualOsuDirectory;
                            else if (!string.IsNullOrEmpty(_cachedOsuPath) && Directory.Exists(_cachedOsuPath))
                                resolvedDir = _cachedOsuPath;

                            if (resolvedDir != null)
                            {
                                OsuDirectory = resolvedDir;
                                SongsPath = OsuUtils.GetSongsFolderLocation(OsuDirectory, string.Empty);
                                IsDirectoryLoaded = true;
                                OnOsuDirectoryLoaded?.Invoke(OsuDirectory);
                            }
                        }

                        if (!IsOsuRunning || !_isMemoryReadReady) continue;
                        if (!IsDirectoryLoaded || !_sreader.CanRead) continue;

                        _sreader.TryRead(_baseAddresses.Beatmap);
                        lock (_playerLock)
                        {
                            _sreader.TryRead(_baseAddresses.Player);
                        }
                        _sreader.TryRead(_baseAddresses.GeneralData);
                        _sreader.TryRead(_baseAddresses.ResultsScreen);

                        var newStatus = _baseAddresses.GeneralData.OsuStatus;
                        CurrentStatus = newStatus;
                        IsPlaying = CurrentStatus == OsuMemoryStatus.Playing;
                        IsResultScreen = CurrentStatus == OsuMemoryStatus.ResultsScreen;
                        CurrentOsuGamemode = CurrentStatus switch
                        {
                            OsuMemoryStatus.Playing => _baseAddresses.Player.Mode,
                            OsuMemoryStatus.ResultsScreen => _baseAddresses.ResultsScreen.Mode,
                            _ => _baseAddresses.GeneralData.GameMode
                        };

                        // ステータス遷移を通知
                        if (newStatus != _prevStatus)
                        {
                            var prev = _prevStatus;
                            _prevStatus = newStatus;
                            OnStatusChanged?.Invoke(prev, newStatus);
                        }

                        OnMemoryRead?.Invoke();
                    }
                    catch (Exception e) { LogUtils.DebugLogger(e.Message, true); }
                }
            })
            { IsBackground = true }.Start();
        }
    }
}
