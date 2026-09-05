using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsuMate.Models;

namespace OsuMate.Utils
{
  internal static class ConfigUtils
  {
    private static readonly string JSON_PATH = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory,
      "Config.json"
    );

    internal static RootConfig LoadRootConfig()
    {
      if (File.Exists(JSON_PATH))
      {
        try
        {
          var json = File.ReadAllText(JSON_PATH);
          var root = JsonSerializer.Deserialize<RootConfig>(json);
          if (root != null)
            return NormalizeRoot(root);
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger("ConfigUtils.LoadRootConfig failed: " + e.Message, true);
        }
      }

      return CreateDefaultRootConfig();
    }

    internal static void SaveRootConfig(RootConfig config)
    {
      try
      {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(JSON_PATH, json);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("ConfigUtils.SaveRootConfig failed: " + e.Message, true);
      }
    }

    internal static GlobalConfig LoadGlobalConfig() => LoadRootConfig().Global;

    private static RootConfig CreateDefaultRootConfig()
    {
      var preset = new Preset { Name = "Default", Config = new PresetConfig() };
      return new RootConfig
      {
        Global = new GlobalConfig(),
        ActivePresetId = preset.Id,
        Presets = [preset],
      };
    }

    private static RootConfig NormalizeRoot(RootConfig root)
    {
      root.Global ??= new GlobalConfig();
      root.Presets ??= [];
      root.Global.TargetPlayerNames ??= [];
      root.Presets = root.Presets.Where(p => p != null).ToList();
      foreach (var preset in root.Presets)
      {
        preset.Config ??= new PresetConfig();
        if (preset.Config.KeyOverlayDurationMs <= 0)
          preset.Config.KeyOverlayDurationMs = 1000;
      }
      if (root.Presets.Count == 0)
        root.Presets.Add(new Preset { Name = "Default", Config = new PresetConfig() });
      if (root.Presets.All(p => p.Id != root.ActivePresetId))
        root.ActivePresetId = root.Presets[0].Id;
      return root;
    }
  }
}
