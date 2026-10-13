using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsuMate.Models;

namespace OsuMate.Utils
{
  internal static class ConfigUtils
  {
    private const int ReadRetryCount = 3;
    private const int ReadRetryDelayMs = 50;

    private static readonly string JSON_PATH = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory,
      "Config.json"
    );

    private static readonly string TEMP_PATH = JSON_PATH + ".tmp";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly object FileLock = new();

    private static volatile bool _saveBlocked;

    internal static RootConfig LoadRootConfig()
    {
      lock (FileLock)
      {
        if (!File.Exists(JSON_PATH))
          return CreateDefaultRootConfig();

        if (!TryReadConfigText(out var json))
        {
          _saveBlocked = true;
          return CreateDefaultRootConfig();
        }

        try
        {
          var root = JsonSerializer.Deserialize<RootConfig>(json);
          if (root != null)
            return NormalizeRoot(root);
        }
        catch (JsonException e)
        {
          LogUtils.DebugLogger(
            "ConfigUtils.LoadRootConfig parse failed: " + e,
            true,
            writeToFile: true
          );
          QuarantineCorruptedConfig();
        }

        return CreateDefaultRootConfig();
      }
    }

    internal static void SaveRootConfig(RootConfig config)
    {
      if (_saveBlocked)
        return;

      try
      {
        var json = JsonSerializer.Serialize(config, WriteOptions);
        lock (FileLock)
        {
          File.WriteAllText(TEMP_PATH, json);
          File.Move(TEMP_PATH, JSON_PATH, overwrite: true);
        }
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("ConfigUtils.SaveRootConfig failed: " + e, true, writeToFile: true);
      }
    }

    private static bool TryReadConfigText(out string json)
    {
      json = "";
      for (var attempt = 1; attempt <= ReadRetryCount; attempt++)
      {
        try
        {
          json = File.ReadAllText(JSON_PATH);
          return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
          if (attempt == ReadRetryCount)
          {
            LogUtils.DebugLogger(
              "ConfigUtils.LoadRootConfig read failed, saving is disabled for this session: " + e,
              true,
              writeToFile: true
            );
            return false;
          }
          Thread.Sleep(ReadRetryDelayMs);
        }
      }
      return false;
    }

    private static void QuarantineCorruptedConfig()
    {
      try
      {
        File.Move(
          JSON_PATH,
          JSON_PATH + $".corrupted-{DateTime.Now:yyyyMMddHHmmss}.bak",
          overwrite: true
        );
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger(
          "ConfigUtils.QuarantineCorruptedConfig failed, saving is disabled for this session: " + e,
          true,
          writeToFile: true
        );
        _saveBlocked = true;
      }
    }

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
          preset.Config.KeyOverlayDurationMs = PresetConfig.DefaultKeyOverlayDurationMs;
      }
      if (root.Presets.Count == 0)
        root.Presets.Add(new Preset { Name = "Default", Config = new PresetConfig() });
      if (root.Presets.All(p => p.Id != root.ActivePresetId))
        root.ActivePresetId = root.Presets[0].Id;
      root.ModePresets ??= new ModePresetConfig();
      root.ModePresets.Normalize(root.Presets.Select(p => p.Id).ToHashSet());
      return root;
    }
  }
}
