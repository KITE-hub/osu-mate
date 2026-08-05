using System.Collections.Generic;

namespace OsuMate.Models
{
    /// <summary>Config.json のルート構造。共通設定とプリセット一覧を持つ。</summary>
    public class RootConfig
    {
        public GlobalConfig Global { get; set; } = new();
        public string ActivePresetId { get; set; } = "";
        public List<Preset> Presets { get; set; } = new();
    }
}
