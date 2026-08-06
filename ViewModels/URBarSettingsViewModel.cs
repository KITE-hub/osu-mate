using OsuMate.Models;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// URBar の有効/無効・回転・サイズ・位置を管理するサブViewModel。
    ///
    /// リファクタリング分析レポート「段階的に進める」項目7に基づき、
    /// 元々 <see cref="SettingsViewModel"/> に混在していた「プリセット対象設定」のうち
    /// URBar 関連部分を切り出したもの（挙動は切り出し前と同一）。
    ///
    /// 対象データは <see cref="PresetConfig"/>（プリセット切り替えで参照先が変わる）のため、
    /// フィールドとして固定参照を持たず、<c>presetConfig</c> デリゲート経由で
    /// 常に「現在アクティブな」設定を読み書きする。
    /// </summary>
    public sealed class URBarSettingsViewModel : ObservableBase
    {
        public event Action? OnSaveURBarPositionRequested;
        public event Action? OnApplyURBarPositionRequested;
        public event Action? OnSaveURBarSizeRequested;
        public event Action? OnApplyURBarSizeRequested;
        public void RequestSaveURBarPosition() => OnSaveURBarPositionRequested?.Invoke();
        public void RequestApplyURBarPosition() => OnApplyURBarPositionRequested?.Invoke();
        public void RequestSaveURBarSize() => OnSaveURBarSizeRequested?.Invoke();
        public void RequestApplyURBarSize() => OnApplyURBarSizeRequested?.Invoke();

        private readonly Func<PresetConfig> _presetConfig;
        private readonly Action _save;
        private readonly Action _debouncedSave;

        /// <param name="presetConfig">現在アクティブなプリセット設定を取得するデリゲート。</param>
        /// <param name="save">即時保存。有効/無効・回転・座標の変更で使う。</param>
        /// <param name="debouncedSave">
        /// デバウンス保存（500ms）。枠ドラッグで連続的に変化する
        /// <see cref="URBarWidth"/>/<see cref="URBarHeight"/> でのみ使用し、
        /// 元実装（SettingsViewModel.DebouncedSave）と同じ挙動を維持する。
        /// </param>
        public URBarSettingsViewModel(Func<PresetConfig> presetConfig, Action save, Action debouncedSave)
        {
            _presetConfig = presetConfig;
            _save = save;
            _debouncedSave = debouncedSave;
        }

        public bool URBarEnabled
        {
            get => _presetConfig().URBarEnabled;
            set { _presetConfig().URBarEnabled = value; OnPropertyChanged(); _save(); }
        }

        public int URBarRotation
        {
            get => _presetConfig().URBarRotation;
            set
            {
                _presetConfig().URBarRotation = ((value % 360) + 360) % 360;
                OnPropertyChanged();
                OnPropertyChanged(nameof(URBarRotationLabel));
                _save();
            }
        }
        public string URBarRotationLabel => $"{_presetConfig().URBarRotation}°";

        public double URBarWidth
        {
            get => _presetConfig().URBarWidth;
            set { _presetConfig().URBarWidth = Math.Max(40, value); OnPropertyChanged(); OnPropertyChanged(nameof(URBarSizeText)); _debouncedSave(); }
        }
        public double URBarHeight
        {
            get => _presetConfig().URBarHeight;
            set { _presetConfig().URBarHeight = Math.Max(20, value); OnPropertyChanged(); OnPropertyChanged(nameof(URBarSizeText)); _debouncedSave(); }
        }

        // URBarX/Y：中心座標
        public double URBarX
        {
            get => _presetConfig().URBarX;
            set { _presetConfig().URBarX = value; OnPropertyChanged(); OnPropertyChanged(nameof(URBarPositionText)); _save(); }
        }
        public double URBarY
        {
            get => _presetConfig().URBarY;
            set { _presetConfig().URBarY = value; OnPropertyChanged(); OnPropertyChanged(nameof(URBarPositionText)); _save(); }
        }

        public string URBarPositionText => $"X: {(int)URBarX}  Y: {(int)URBarY}";
        public string URBarSizeText => $"W: {(int)URBarWidth}  H: {(int)URBarHeight}";

        /// <summary>
        /// 平均線目標値のEMAにおける最新打鍵の重み（0～1）。Slider経由で連続的に変化するため
        /// URBarWidth/Heightと同様にデバウンス保存を使う。
        /// </summary>
        public double URBarAvgLineFollowStrength
        {
            get => _presetConfig().URBarAvgLineFollowStrength;
            set { _presetConfig().URBarAvgLineFollowStrength = Math.Clamp(value, 0, 1); OnPropertyChanged(); _debouncedSave(); }
        }

        /// <summary>平均線が新しい目標値へ移動するアニメーション時間（ミリ秒）。Textbox経由（LostFocusで確定）のため即時保存。</summary>
        public double URBarAvgLineAnimMs
        {
            get => _presetConfig().URBarAvgLineAnimMs;
            set { _presetConfig().URBarAvgLineAnimMs = Math.Max(0, value); OnPropertyChanged(); _save(); }
        }

        /// <summary>ラベル（判定幅の数値、EARLY/LATE）の不透明度（0～1）。Slider経由で連続的に変化するためデバウンス保存を使う。</summary>
        public double URBarLabelOpacity
        {
            get => _presetConfig().URBarLabelOpacity;
            set { _presetConfig().URBarLabelOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); _debouncedSave(); }
        }
        /// <summary>barThick（判定色帯）の不透明度（0～1）。Slider経由で連続的に変化するためデバウンス保存を使う。</summary>
        public double URBarSegmentOpacity
        {
            get => _presetConfig().URBarSegmentOpacity;
            set { _presetConfig().URBarSegmentOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); _debouncedSave(); }
        }

        /// <summary>白線（中心線）と赤マーカー（Avg）の不透明度（0～1）。Slider経由で連続的に変化するためデバウンス保存を使う。</summary>
        public double URBarMarkerOpacity
        {
            get => _presetConfig().URBarMarkerOpacity;
            set { _presetConfig().URBarMarkerOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); _debouncedSave(); }
        }

        /// <summary>判定ドットの不透明度（0～1）。Slider経由で連続的に変化するためデバウンス保存を使う。</summary>
        public double URBarHitErrorOpacity
        {
            get => _presetConfig().URBarHitErrorOpacity;
            set { _presetConfig().URBarHitErrorOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); _debouncedSave(); }
        }

        /// <summary>
        /// プリセット切り替え時に <see cref="SettingsViewModel"/> から呼ばれる。全プロパティの変更通知を行う。
        /// </summary>
        public void NotifyPresetApplied()
        {
            OnPropertyChanged(nameof(URBarEnabled));
            OnPropertyChanged(nameof(URBarRotation));
            OnPropertyChanged(nameof(URBarRotationLabel));
            OnPropertyChanged(nameof(URBarWidth));
            OnPropertyChanged(nameof(URBarHeight));
            OnPropertyChanged(nameof(URBarX));
            OnPropertyChanged(nameof(URBarY));
            OnPropertyChanged(nameof(URBarPositionText));
            OnPropertyChanged(nameof(URBarSizeText));
            OnPropertyChanged(nameof(URBarAvgLineFollowStrength));
            OnPropertyChanged(nameof(URBarAvgLineAnimMs));
            OnPropertyChanged(nameof(URBarLabelOpacity));
            OnPropertyChanged(nameof(URBarSegmentOpacity));
            OnPropertyChanged(nameof(URBarMarkerOpacity));
            OnPropertyChanged(nameof(URBarHitErrorOpacity));
        }
    }
}
