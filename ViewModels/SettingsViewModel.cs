using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.PlayLog;
using OsuMate.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OsuMate.ViewModels
{
    public class SettingsViewModel : ObservableBase
    {
        /// <summary>In-Game Overlay の位置・表示・項目管理</summary>
        public OverlaySettingsViewModel Overlay { get; }

        /// <summary>URBar の位置・サイズ・回転管理</summary>
        public URBarSettingsViewModel URBar { get; }

        /// <summary>osu mate/osu! 本体のウィンドウ位置管理</summary>
        public PositionSettingsViewModel Position { get; }

        /// <summary>Log タブのカラム設定（定義・順序・有効/無効）</summary>
        public LogColumnSettings LogColumnSettings { get; }

        /// <summary>Log絞り込み・Best pp算出の対象プレイヤー名一覧（共通設定）</summary>
        public ObservableCollection<string> TargetPlayerNames { get; } = [];

        /// <summary>設定済みのプリセット一覧</summary>
        public ObservableCollection<Preset> Presets => _presetManager.Presets;

        public IReadOnlyList<string> AvailableFonts { get; } =
            OsuMate.Utils.AppFonts.EmbeddedFontNames
                 // 埋め込みフォントとインストール済みシステムフォントの間に区切り線を表示
                 .Append(OsuMate.Utils.AppFonts.FontListSeparator)
                 .Concat(Fonts.SystemFontFamilies
                     .Select(f => f.Source)
                     .Where(s => !OsuMate.Utils.AppFonts.EmbeddedFontNames.Contains(s))
                     .OrderBy(s => s))
                 .ToList();

        // ─── Overlay関連（委譲） ─────────────────────────────────────────
        public event Action? OnSaveOverlayPositionRequested
        {
            add => Overlay.OnSaveOverlayPositionRequested += value;
            remove => Overlay.OnSaveOverlayPositionRequested -= value;
        }
        public event Action? OnApplyOverlayPositionRequested
        {
            add => Overlay.OnApplyOverlayPositionRequested += value;
            remove => Overlay.OnApplyOverlayPositionRequested -= value;
        }
        public void RequestSaveOverlayPosition() => Overlay.RequestSaveOverlayPosition();
        public void RequestApplyOverlayPosition() => Overlay.RequestApplyOverlayPosition();

        // ─── URBar関連（委譲） ───────────────────────────────────────────
        public event Action? OnSaveURBarPositionRequested
        {
            add => URBar.OnSaveURBarPositionRequested += value;
            remove => URBar.OnSaveURBarPositionRequested -= value;
        }
        public event Action? OnApplyURBarPositionRequested
        {
            add => URBar.OnApplyURBarPositionRequested += value;
            remove => URBar.OnApplyURBarPositionRequested -= value;
        }
        public event Action? OnSaveURBarSizeRequested
        {
            add => URBar.OnSaveURBarSizeRequested += value;
            remove => URBar.OnSaveURBarSizeRequested -= value;
        }
        public event Action? OnApplyURBarSizeRequested
        {
            add => URBar.OnApplyURBarSizeRequested += value;
            remove => URBar.OnApplyURBarSizeRequested -= value;
        }
        public void RequestSaveURBarPosition() => URBar.RequestSaveURBarPosition();
        public void RequestApplyURBarPosition() => URBar.RequestApplyURBarPosition();
        public void RequestSaveURBarSize() => URBar.RequestSaveURBarSize();
        public void RequestApplyURBarSize() => URBar.RequestApplyURBarSize();

        private readonly RootConfig _root;
        private readonly GlobalConfig _globalConfig;
        private readonly OsuMemoryService _memory;
        private readonly PlayLogRepository _playLogRepository;
        private readonly PresetManager _presetManager;

        /// <summary>現在アクティブなプリセットの設定。実体は <see cref="PresetManager.ActiveConfig"/>。</summary>
        private PresetConfig _presetConfig => _presetManager.ActiveConfig;
        private System.Threading.Timer? _saveTimer;

        public SettingsViewModel(OsuMemoryService memory, PlayLogRepository playLogRepository)
        {
            _memory = memory;
            _playLogRepository = playLogRepository;
            _root = ConfigUtils.LoadRootConfig();
            _globalConfig = _root.Global;

            // 前回セッションで自動検出されConfigに保存済みのosu!.exeディレクトリを、
            // OsuMemoryServiceのディレクトリ確定処理（プロセス自動検出より優先される）に反映する。
            // これにより、今回osu!を起動していなくても前回検出済みのパスをそのまま使える。
            _memory.ManualOsuDirectory = _globalConfig.OsuExeDirectory;

            // Log Directoryの設定もPlayLogRepositoryに即座に反映しておく
            // （再起動しないと反映されないと、変更前のディレクトリを覚えていないまま
            //  再起動後に「そのディレクトリにJSONが無い」扱いになってしまうため）
            _playLogRepository.SetLogOutputDirOverride(_globalConfig.LogOutputDir);

            _memory.OnOsuDirectoryLoaded += dir =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    OsuExeDirectory = dir;
                });
            };

            _presetManager = new PresetManager(_root);

            // サブViewModelは _presetManager 構築後に生成する（Func<PresetConfig> が
            // _presetConfig（内部で _presetManager.ActiveConfig を参照）に依存するため）。
            // 各サブVMのPropertyChangedは同名のままSettingsViewModel自身にも中継し、
            // WindowManagerService/MainWindow.xaml.cs 等の既存の
            // 「nameof(SettingsViewModel.Xxx) との比較」がそのまま機能するようにする。
            Overlay = new OverlaySettingsViewModel(() => _presetConfig, Save, DebouncedSave);
            Overlay.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

            URBar = new URBarSettingsViewModel(() => _presetConfig, Save, DebouncedSave);
            URBar.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

            Position = new PositionSettingsViewModel(() => _presetConfig, Save);
            Position.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

            _presetManager.ActivePresetChanged += ApplyPresetConfig;

            foreach (var name in _globalConfig.TargetPlayerNames) TargetPlayerNames.Add(name);

            LogColumnSettings = new LogColumnSettings(_globalConfig, Save);
        }

        // ─── プリセット管理 ─────────────────────────────────────────────
        // 実処理は PresetManager に委譲する。ここでは「切り替わったときに画面へどう反映するか」
        // （OnPropertyChangedの一括発火・ウィンドウへの適用要求）だけを担当する。

        /// <summary>
        /// プリセット選択ComboBoxの SelectedItem に直接バインドする（オブジェクト参照ベース）。
        /// SelectedValue＋SelectedValuePath による文字列ID照合は、ItemsSource の反映タイミング次第で
        /// 初期選択が解決されず表示が空欄になることがあるため、より確実な SelectedItem 方式に統一する。
        /// </summary>
        public Preset? SelectedPreset
        {
            get => _presetManager.SelectedPreset;
            set => _presetManager.SelectedPreset = value;
        }

        /// <summary>現在の設定内容で新規プリセットを作成する（Overlay等は初期値になる）。作成後は自動的に切り替える。</summary>
        public Preset CreatePreset(string name) => _presetManager.CreatePreset(name);

        /// <summary>指定したプリセットの内容を複製した新規プリセットを作成する。作成後は自動的に切り替える。</summary>
        public Preset DuplicatePreset(string sourceId, string newName) => _presetManager.DuplicatePreset(sourceId, newName);

        public void RenamePreset(string id, string newName) => _presetManager.RenamePreset(id, newName);

        /// <summary>プリセットを削除する。最後の1件は削除できない。削除対象が現在選択中の場合は先頭に切り替える。</summary>
        public bool DeletePreset(string id) => _presetManager.DeletePreset(id);

        /// <summary>
        /// プリセット切り替え時の即時反映本体。PresetManager.ActivePresetChanged から呼ばれる。
        /// 各サブViewModelの NotifyPresetApplied() を呼ぶことで、そちらのプロパティ変更通知
        /// （SettingsViewModel自身へも同名で中継される）に反映処理を委ねる。
        /// 既存の購読先（WindowManagerService / MainWindow.xaml.cs 等）の反応経路は変わらない。
        /// </summary>
        private void ApplyPresetConfig(PresetConfig config)
        {
            Overlay.NotifyPresetApplied();
            URBar.NotifyPresetApplied();
            Position.NotifyPresetApplied();
            OnPropertyChanged(nameof(SelectedPreset));

            // 実ウィンドウの位置・サイズも、設定画面の Apply ボタンと同じ経路で反映する
            RequestApplyOverlayPosition();
            RequestApplyURBarPosition();
            RequestApplyURBarSize();
        }

        // ─── 対象プレイヤー名（共通） ─────────────────────────────────────

        public void AddTargetPlayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (TargetPlayerNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

            TargetPlayerNames.Add(name);
            _globalConfig.TargetPlayerNames = [.. TargetPlayerNames];
            Save();
        }

        public void RemoveTargetPlayerName(string name)
        {
            var existing = TargetPlayerNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            TargetPlayerNames.Remove(existing);
            _globalConfig.TargetPlayerNames = [.. TargetPlayerNames];
            Save();
        }

        // ─── プリセット対象設定（Overlay/URBar/App・Osu位置）
        public double OverlayX { get => Overlay.OverlayX; set => Overlay.OverlayX = value; }
        public double OverlayY { get => Overlay.OverlayY; set => Overlay.OverlayY = value; }
        public string OverlayPositionText => Overlay.OverlayPositionText;
        public bool OverlayEnabled { get => Overlay.OverlayEnabled; set => Overlay.OverlayEnabled = value; }
        public double OverlayFontSize { get => Overlay.OverlayFontSize; set => Overlay.OverlayFontSize = value; }
        public bool IsShowValueFirst { get => Overlay.IsShowValueFirst; set => Overlay.IsShowValueFirst = value; }

        public bool URBarEnabled { get => URBar.URBarEnabled; set => URBar.URBarEnabled = value; }
        public int URBarRotation { get => URBar.URBarRotation; set => URBar.URBarRotation = value; }
        public string URBarRotationLabel => URBar.URBarRotationLabel;
        public double URBarWidth { get => URBar.URBarWidth; set => URBar.URBarWidth = value; }
        public double URBarHeight { get => URBar.URBarHeight; set => URBar.URBarHeight = value; }
        public double URBarX { get => URBar.URBarX; set => URBar.URBarX = value; }
        public double URBarY { get => URBar.URBarY; set => URBar.URBarY = value; }
        public string URBarPositionText => URBar.URBarPositionText;
        public string URBarSizeText => URBar.URBarSizeText;
        public double URBarAvgLineFollowStrength { get => URBar.URBarAvgLineFollowStrength; set => URBar.URBarAvgLineFollowStrength = value; }
        public double URBarAvgLineAnimMs { get => URBar.URBarAvgLineAnimMs; set => URBar.URBarAvgLineAnimMs = value; }
        public double URBarLabelOpacity { get => URBar.URBarLabelOpacity; set => URBar.URBarLabelOpacity = value; }
        public double URBarSegmentOpacity { get => URBar.URBarSegmentOpacity; set => URBar.URBarSegmentOpacity = value; }
        public double URBarMarkerOpacity { get => URBar.URBarMarkerOpacity; set => URBar.URBarMarkerOpacity = value; }
        public double URBarHitErrorOpacity { get => URBar.URBarHitErrorOpacity; set => URBar.URBarHitErrorOpacity = value; }

        public bool AppPositionEnabled { get => Position.AppPositionEnabled; set => Position.AppPositionEnabled = value; }
        public double AppX { get => Position.AppX; set => Position.AppX = value; }
        public double AppY { get => Position.AppY; set => Position.AppY = value; }
        public string AppPositionText => Position.AppPositionText;
        public void SetAppPosition(double x, double y) => Position.SetAppPosition(x, y);

        public bool OsuPositionEnabled { get => Position.OsuPositionEnabled; set => Position.OsuPositionEnabled = value; }
        public double OsuX { get => Position.OsuX; set => Position.OsuX = value; }
        public double OsuY { get => Position.OsuY; set => Position.OsuY = value; }
        public string OsuPositionText => Position.OsuPositionText;
        public void SetOsuPosition(double x, double y) => Position.SetOsuPosition(x, y);

        /// <summary>osu mate・osu!両方のスタートアップ位置を一括オン/オフ</summary>
        public bool StartupPositionEnabled { get => Position.StartupPositionEnabled; set => Position.StartupPositionEnabled = value; }

        // ─── Overlay Items（Item Priority）── 委譲 ──────────────────────
        public ObservableCollection<OverlayItem> Items => Overlay.Items;
        public void MoveItem(int fromIndex, int toIndex) => Overlay.MoveItem(fromIndex, toIndex);
        public void ToggleItem(OverlayItem item) => Overlay.ToggleItem(item);

        // ─── 共通設定（Overall） ─────────────────────────────────────────

        public string FontFamily
        {
            get => _globalConfig.FontFamily;
            set
            {
                // ComboBox上の区切り線（セパレータ）は選択不可にしているが、念のための防御。
                if (value == OsuMate.Utils.AppFonts.FontListSeparator)
                    return;
                _globalConfig.FontFamily = value; OnPropertyChanged(); Save();
            }
        }
        public bool IsDarkMode
        {
            get => _globalConfig.IsDarkMode;
            set { _globalConfig.IsDarkMode = value; OnPropertyChanged(); Save(); }
        }

        /// <summary>
        /// Fast Lane（メモリ読み取り）/ Slow Lane（pp・Strain計算）が共有する更新間隔（ms）。
        /// 8〜200msにClampする（0以下はビジーループ化、極端な大値はURBar等が実用に耐えないため）。
        /// </summary>
        public int DataUpdateIntervalMs
        {
            get => _globalConfig.DataUpdateIntervalMs;
            set
            {
                int clamped = Math.Clamp(value, 8, 200);
                if (_globalConfig.DataUpdateIntervalMs == clamped) return;
                _globalConfig.DataUpdateIntervalMs = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DataUpdateIntervalFpsText));
                Save();
            }
        }

        /// <summary>
        /// <see cref="DataUpdateIntervalMs"/>をfps換算で表示するためのテキスト。
        /// </summary>
        public string DataUpdateIntervalFpsText => $"(≈{1000.0 / DataUpdateIntervalMs:0.#} fps)";

        /// <summary>
        /// osu!.exeが格納されているフォルダ。
        /// 起動中のosu!プロセスから自動検出する。
        /// (osu!非起動時でもプレイ履歴の集計を行うため保持)
        /// 自動起動用の指定（<see cref="AutoLaunchOsuPath"/>）とは完全に独立している。
        /// </summary>
        public string OsuExeDirectory
        {
            get => _globalConfig.OsuExeDirectory;
            set
            {
                var normalized = value?.Trim() ?? "";
                if (_globalConfig.OsuExeDirectory == normalized) return;
                _globalConfig.OsuExeDirectory = normalized;
                // OsuMemoryService側のディレクトリ確定処理（DataUpdateIntervalMsごとのループ）に即座に反映されるようにする
                _memory.ManualOsuDirectory = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OsuExeDirectoryText));
                Save();
            }
        }

        /// <summary>
        /// osu!.exe のパス表示用（TextBlock）。ディレクトリだけだと何を指しているか分かりにくいため、
        /// "osu!.exe" を付けたフルパスを表示する。
        /// 確定済みのディレクトリがあればそれを暫定表示し、未確定の場合は
        /// "(Auto-detecting...)" と表示
        /// </summary>
        public string OsuExeDirectoryText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(OsuExeDirectory))
                    return Path.Combine(OsuExeDirectory, "osu!.exe");

                if (!string.IsNullOrWhiteSpace(_memory.OsuDirectory))
                    return Path.Combine(_memory.OsuDirectory, "osu!.exe");

                return "(Auto-detecting...)";
            }
        }

        /// <summary>
        /// <see cref="AutoLaunchOsuPath"/> による自動起動機能そのものの有効/無効。
        /// </summary>
        public bool AutoLaunchOsuEnabled
        {
            get => _globalConfig.AutoLaunchOsuEnabled;
            set
            {
                if (_globalConfig.AutoLaunchOsuEnabled == value) return;
                _globalConfig.AutoLaunchOsuEnabled = value;
                OnPropertyChanged();
                Save();
            }
        }

        /// <summary>
        /// osu mate起動時に自動的に起動したい osu! の実行ファイル（.exe）またはショートカット（.lnk）への
        /// フルパス。空文字の場合は自動起動しない。
        /// </summary>
        public string AutoLaunchOsuPath
        {
            get => _globalConfig.AutoLaunchOsuPath;
            set
            {
                var normalized = value?.Trim() ?? "";
                if (_globalConfig.AutoLaunchOsuPath == normalized) return;
                _globalConfig.AutoLaunchOsuPath = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoLaunchOsuPathText));
                Save();
            }
        }

        /// <summary>Auto Launch osu のパス表示用（TextBlock）。未設定時はその旨を表示する。</summary>
        public string AutoLaunchOsuPathText =>
            string.IsNullOrWhiteSpace(AutoLaunchOsuPath) ? "(Do not auto-launch)" : AutoLaunchOsuPath;

        /// <summary>
        /// プレイ履歴JSONの出力先フォルダ。空文字の場合は実行ファイルと同じ場所の PlayLogs/ を使う
        /// （PlayLogRepository.LogOutputDir と同じ既定値）。
        /// 変更した瞬間に PlayLogRepository へも反映し、変更前のディレクトリにあった
        /// 既存のJSONファイルは新しいディレクトリへ移動する。
        /// </summary>
        public string LogOutputDir
        {
            get => _globalConfig.LogOutputDir;
            set
            {
                var normalized = value?.Trim() ?? "";
                if (_globalConfig.LogOutputDir == normalized) return;

                // 変更前に実際に使われていた保存先を控えておく
                var oldDir = _playLogRepository.LogOutputDir;

                _globalConfig.LogOutputDir = normalized;
                _playLogRepository.SetLogOutputDirOverride(normalized);
                var newDir = _playLogRepository.LogOutputDir;

                _playLogRepository.MigrateLogFiles(oldDir, newDir);

                OnPropertyChanged();
                OnPropertyChanged(nameof(LogOutputDirText));
                Save();
            }
        }

        /// <summary>Log Directory のパス表示用（TextBlock）。未指定時は既定の PlayLogs フォルダを表示する。</summary>
        public string LogOutputDirText =>
            string.IsNullOrWhiteSpace(LogOutputDir)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayLogs")
                : LogOutputDir;

        /// <summary>
        /// Logタブに中断（リザルト画面まで完了しなかった）プレイも表示するかどうか。
        /// false にすると、PlayLogViewModel側のフィルタで一覧から除外される。
        /// </summary>
        public bool ShowAbortedPlays
        {
            get => _globalConfig.ShowAbortedPlays;
            set
            {
                if (_globalConfig.ShowAbortedPlays == value) return;
                _globalConfig.ShowAbortedPlays = value;
                OnPropertyChanged();
                Save();
            }
        }

        private void DebouncedSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(_ =>
            {
                // Timerコールバック（ThreadPool上）で例外が捕捉されないとプロセス全体が落ちるため、
                // ここで確実に捕捉してログに残す。
                try { Save(); }
                catch (Exception e) { LogUtils.DebugLogger("SettingsViewModel.DebouncedSave failed: " + e.Message, true); }
            }, null, 500, Timeout.Infinite);
        }

        public void Save()
        {
            _presetConfig.InGameOverlayPriority = Overlay.ToPriorityString();
            _globalConfig.LogColumnPriority = LogColumnSettings.ToLogColumnPriorityString();
            _presetManager.Save();
        }
    }
}
