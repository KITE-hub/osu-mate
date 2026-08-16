namespace OsuMate.Models
{
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

    public double URBarAvgLineFollowStrength { get; set; } = 0.1;

    public double URBarAvgLineAnimMs { get; set; } = 800;

    public double URBarLabelOpacity { get; set; } = 0.5;

    public double URBarSegmentOpacity { get; set; } = 0.2;

    public double URBarMarkerOpacity { get; set; } = 0.75;

    public double URBarHitErrorOpacity { get; set; } = 1.0;

    public bool AppPositionEnabled { get; set; } = true;
    public double AppX { get; set; } = 1300;
    public double AppY { get; set; } = 0;

    public bool OsuPositionEnabled { get; set; } = true;
    public double OsuX { get; set; } = 0;
    public double OsuY { get; set; } = 0;
  }
}
