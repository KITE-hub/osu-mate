using System.Collections.Generic;

namespace OsuMate.Models
{
  public class GlobalConfig
  {
    public string FontFamily { get; set; } = "Oxanium";
    public bool IsDarkMode { get; set; } = true;

    public List<string> TargetPlayerNames { get; set; } = new();

    public string LogColumnPriority { get; set; } = "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15";

    public bool ShowAbortedPlays { get; set; } = true;

    public string OsuExeDirectory { get; set; } = "";

    public string AutoLaunchOsuPath { get; set; } = "";

    public bool AutoLaunchOsuEnabled { get; set; } = true;

    public bool AdjustPitchWithSpeed { get; set; } = false;

    public bool IsRandomEnabled { get; set; } = false;

    public decimal BatchStartRate { get; set; } = 1.05M;

    public decimal BatchStep { get; set; } = 0.05M;

    public int BatchCount { get; set; } = 4;

    public int DataUpdateIntervalMs { get; set; } = 33;
  }
}
