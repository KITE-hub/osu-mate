using System.Collections.Generic;

namespace OsuMate.Models
{
  public class RootConfig
  {
    public GlobalConfig Global { get; set; } = new();
    public string ActivePresetId { get; set; } = "";
    public List<Preset> Presets { get; set; } = new();
  }
}
