namespace OsuMate.Models
{
    /// <summary>
    /// プリセットごとに切り替わる設定。
    /// Startup Window Position / URBar / In-Game Overlay のみを含む。
    /// （Overall・環境依存の設定は <see cref="GlobalConfig"/> 側）
    /// </summary>
    public class PresetConfig
    {
        public double OverlayX { get; set; } = 1000;
        public double OverlayY { get; set; } = 400;
        public bool OverlayEnabled { get; set; } = true;
        public double OverlayFontSize { get; set; } = 24;
        public bool IsShowValueFirst { get; set; } = false;
        public string InGameOverlayPriority { get; set; } = "2/3/4/15/6/8/10";

        public bool URBarEnabled { get; set; } = true;
        public int URBarRotation { get; set; } = 0;
        public double URBarWidth { get; set; } = 700;
        public double URBarHeight { get; set; } = 200;
        public double URBarX { get; set; } = 300;
        public double URBarY { get; set; } = 500;

        /// <summary>
        /// 平均線（直近打鍵位置の目標値、EMA=指数移動平均）における、最新打鍵の重み。
        /// 0=最新打鍵を無視して平均が動かない、1=EMAを無効化し常に最新打鍵へ瞬時追従。
        /// 既定値0.1はosu!(lazer)のBarHitErrorMeterに準拠。
        /// </summary>
        public double URBarAvgLineFollowStrength { get; set; } = 0.1;

        /// <summary>
        /// 平均線が新しい目標値へ移動するアニメーション時間（ミリ秒）。0で瞬時移動。
        /// 既定値800はosu!(lazer)のBarHitErrorMeter.OnNewJudgement（arrow.MoveToY(..., 800, ...)）に準拠。
        /// </summary>
        public double URBarAvgLineAnimMs { get; set; } = 800;

        /// <summary>ラベル（判定幅の数値、EARLY/LATE）の不透明度（0～1）。</summary>
        public double URBarLabelOpacity { get; set; } = 0.5;

        /// <summary>barThick（判定色帯）の不透明度（0～1）。</summary>
        public double URBarSegmentOpacity { get; set; } = 0.2;

        /// <summary>白線（中心線）と赤マーカー（Avg）の不透明度（0～1）。</summary>
        public double URBarMarkerOpacity { get; set; } = 0.75;

        /// <summary>判定ドットの不透明度（0～1）。</summary>
        public double URBarHitErrorOpacity { get; set; } = 1.0;

        public bool AppPositionEnabled { get; set; } = true;
        public double AppX { get; set; } = 1300;
        public double AppY { get; set; } = 0;

        public bool OsuPositionEnabled { get; set; } = true;
        public double OsuX { get; set; } = 0;
        public double OsuY { get; set; } = 0;
    }
}
