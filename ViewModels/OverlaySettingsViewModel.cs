using OsuMate.Models;
using OsuMate.Utils;
using System.Collections.ObjectModel;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// In-Game Overlay の位置・表示設定（座標・有効/無効・フォントサイズ）と、
    /// 表示項目（Item Priority）の並び順・有効/無効を管理するサブViewModel。
    ///
    /// リファクタリング分析レポート「段階的に進める」項目7に基づき、
    /// 元々 <see cref="SettingsViewModel"/> に混在していた「プリセット対象設定」のうち
    /// Overlay 関連部分を切り出したもの（挙動は切り出し前と同一）。
    ///
    /// 対象データは <see cref="PresetConfig"/>（プリセット切り替えで参照先が変わる）のため、
    /// フィールドとして固定参照を持たず、<c>presetConfig</c> デリゲート経由で
    /// 常に「現在アクティブな」設定を読み書きする。
    /// </summary>
    public sealed class OverlaySettingsViewModel : ObservableBase
    {
        private static readonly (int id, string label)[] ItemDefinitions =
        [
            (1,  "SR"),
            (2,  "SS pp"),
            (3,  "Lossmode pp"),
            (4,  "Predicted pp"),
            (5,  "Current pp"),
            (6,  "Accuracy"),
            (7,  "Hits"),
            (8,  "Avg offset"),
            (9,  "Universal offset help"),
            (10, "Local offset help"),
            (11, "UR"),
            (12, "Progress %"),
            (13, "Remaining notes"),
            (14, "BPM"),
            (15, "Best pp"),
        ];

        public ObservableCollection<OverlayItem> Items { get; } = [];

        public event Action? OnSaveOverlayPositionRequested;
        public event Action? OnApplyOverlayPositionRequested;
        public void RequestSaveOverlayPosition() => OnSaveOverlayPositionRequested?.Invoke();
        public void RequestApplyOverlayPosition() => OnApplyOverlayPositionRequested?.Invoke();

        private readonly Func<PresetConfig> _presetConfig;
        private readonly Action _save;
        private readonly Action _debouncedSave;

        /// <summary>
        /// Items はUIスレッド（MoveItem/ToggleItemによるドラッグ並び替え等）と
        /// バックグラウンドスレッド（Timerコールバック経由のToPriorityStringでの列挙）の
        /// 双方からアクセスされるため、排他制御が必要（切り出し前の実装をそのまま維持）。
        /// </summary>
        private readonly object _itemsLock = new();

        /// <param name="presetConfig">現在アクティブなプリセット設定を取得するデリゲート。</param>
        /// <param name="save">即時保存。座標・有効/無効の変更で使う。</param>
        /// <param name="debouncedSave">
        /// デバウンス保存（500ms）。Sliderで連続的に変化する <see cref="OverlayFontSize"/> でのみ使用し、
        /// 元実装（SettingsViewModel.DebouncedSave）と同じ挙動を維持する。
        /// </param>
        public OverlaySettingsViewModel(Func<PresetConfig> presetConfig, Action save, Action debouncedSave)
        {
            _presetConfig = presetConfig;
            _save = save;
            _debouncedSave = debouncedSave;
            LoadItems();
        }

        // OverlayX/Y：中心座標
        public double OverlayX
        {
            get => _presetConfig().OverlayX;
            set { _presetConfig().OverlayX = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverlayPositionText)); _save(); }
        }
        public double OverlayY
        {
            get => _presetConfig().OverlayY;
            set { _presetConfig().OverlayY = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverlayPositionText)); _save(); }
        }
        public string OverlayPositionText => $"X: {(int)OverlayX}  Y: {(int)OverlayY}";

        public bool OverlayEnabled
        {
            get => _presetConfig().OverlayEnabled;
            set { _presetConfig().OverlayEnabled = value; OnPropertyChanged(); _save(); }
        }

        public double OverlayFontSize
        {
            get => _presetConfig().OverlayFontSize;
            set { _presetConfig().OverlayFontSize = value; OnPropertyChanged(); _debouncedSave(); }
        }

        /// <summary>
        /// InGameOverlayWindow に表示される各行（Label/Value）の並び順を Value→Label にするかどうか。
        /// false（既定）の場合は Label→Value の並び順。
        /// </summary>
        public bool IsShowValueFirst
        {
            get => _presetConfig().IsShowValueFirst;
            set { _presetConfig().IsShowValueFirst = value; OnPropertyChanged(); _save(); }
        }

        public void LoadItems()
        {
            try
            {
                var priority = _presetConfig().InGameOverlayPriority ?? "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15";
                var tokens = priority.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // 順序付きで有効/無効を解析（"-id" = 無効、"id" = 有効）。
                // 旧形式（有効IDのみを列挙、"-"なし）の文字列もそのまま解釈できる
                // （すべて Enabled=true として読まれ、そこに無い項目は下の未登録分岐で
                //   IsEnabled=false として追加されるため、旧仕様と同じ結果になる）。
                var ordered = tokens
                    .Select(t =>
                    {
                        bool enabled = !t.StartsWith('-');
                        string raw = enabled ? t : t[1..];
                        if (int.TryParse(raw, out int id))
                            return (Id: id, Enabled: enabled);
                        return (Id: -1, Enabled: false);
                    })
                    .Where(x => x.Id > 0)
                    .ToList();

                var orderedIds = ordered.Select(x => x.Id).ToList();

                lock (_itemsLock)
                {
                    Items.Clear();
                    foreach (var item in ordered)
                    {
                        var def = ItemDefinitions.FirstOrDefault(d => d.id == item.Id);
                        if (def == default) continue;
                        Items.Add(new OverlayItem { Id = item.Id, Label = def.label, IsEnabled = item.Enabled });
                    }
                    // 文字列に登場しない項目（未登録／将来追加分）は末尾に無効状態で追加（旧仕様を維持）
                    foreach (var def in ItemDefinitions)
                    {
                        if (orderedIds.Contains(def.id)) continue;
                        Items.Add(new OverlayItem { Id = def.id, Label = def.label, IsEnabled = false });
                    }
                }
            }
            catch (Exception e)
            {
                LogUtils.DebugLogger("OverlaySettingsViewModel.LoadItems failed: " + e.Message, true);
                LoadDefaultItems();
            }
        }

        private void LoadDefaultItems()
        {
            lock (_itemsLock)
            {
                Items.Clear();
                foreach (var def in ItemDefinitions)
                    Items.Add(new OverlayItem { Id = def.id, Label = def.label, IsEnabled = true });
            }
        }

        public void MoveItem(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            if (fromIndex < 0 || fromIndex >= Items.Count) return;
            if (toIndex < 0 || toIndex >= Items.Count) return;
            lock (_itemsLock)
            {
                var item = Items[fromIndex];
                Items.RemoveAt(fromIndex);
                Items.Insert(toIndex, item);
            }
            _save();
        }

        public void ToggleItem(OverlayItem item)
        {
            lock (_itemsLock) { item.IsEnabled = !item.IsEnabled; }
            _save();
        }

        public string ToPriorityString()
        {
            lock (_itemsLock)
                return string.Join("/", Items.Select(i => i.IsEnabled ? i.Id.ToString() : $"-{i.Id}"));
        }

        /// <summary>
        /// プリセット切り替え時に <see cref="SettingsViewModel"/> から呼ばれる。
        /// 全プロパティの変更通知とItemsの再読み込みを行う。
        /// </summary>
        public void NotifyPresetApplied()
        {
            OnPropertyChanged(nameof(OverlayX));
            OnPropertyChanged(nameof(OverlayY));
            OnPropertyChanged(nameof(OverlayPositionText));
            OnPropertyChanged(nameof(OverlayEnabled));
            OnPropertyChanged(nameof(OverlayFontSize));
            OnPropertyChanged(nameof(IsShowValueFirst));
            LoadItems();
        }
    }
}
