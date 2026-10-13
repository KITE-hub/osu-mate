using System.IO;
using OsuMate.Models;
using OsuMate.Utils;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;

namespace OsuMate.Services.Osu
{

  internal static class OsuUtils
  {
    private static readonly Dictionary<int, string> osu_mods = new()
    {
      { 0, "NM" },
      { 1, "NF" },
      { 2, "EZ" },
      { 4, "TD" },
      { 8, "HD" },
      { 16, "HR" },
      { 32, "SD" },
      { 64, "DT" },
      { 128, "RX" },
      { 256, "HT" },
      { 512, "NC" },
      { 1024, "FL" },
      { 2048, "AT" },
      { 4096, "SO" },
      { 8192, "RX2" },
      { 16384, "PF" },
      { 32768, "4K" },
      { 65536, "5K" },
      { 131072, "6K" },
      { 262144, "7K" },
      { 524288, "8K" },
      { 1048576, "FI" },
      { 2097152, "RD" },
      { 4194304, "CM" },
      { 8388608, "TP" },
      { 16777216, "9K" },
      { 33554432, "CP" },
      { 67108864, "1K" },
      { 134217728, "3K" },
      { 268435456, "2K" },
      { 536870912, "SV2" },
      { 1073741824, "MR" },
    };

    private static readonly Dictionary<string, string> LazerAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
      { "RX2", "AP" },
      { "CM", "CN" },
    };

    internal static string ConvertHits(int mode, HitsResult hits)
    {
      return mode switch
      {
        0 => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
        1 => $"{hits.Hit300}/{hits.Hit100}/{hits.HitMiss}",
        2 => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
        3 => $"{hits.HitGeki}/{hits.Hit300}/{hits.HitKatu}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
        _ => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
      };
    }

    internal static Mods ParseMods(int mods)
    {
      List<string> activeModsCalc = [];
      List<string> activeModsShow = [];

      for (int i = 0; i < 32; i++)
      {
        int bit = 1 << i;
        if ((mods & bit) != bit)
          continue;
        if (osu_mods.TryGetValue(bit, out var modStr))
        {
          activeModsCalc.Add(LazerAcronyms.GetValueOrDefault(modStr, modStr).ToLowerInvariant());
          activeModsShow.Add(modStr);
        }
      }

      if (activeModsCalc.Contains("nc") && activeModsCalc.Contains("dt"))
        activeModsCalc.Remove("nc");
      if (activeModsShow.Contains("NC") && activeModsShow.Contains("DT"))
        activeModsShow.Remove("DT");
      if (activeModsShow.Count == 0)
        activeModsShow.Add("NM");

      return new Mods() { Calculation = [.. activeModsCalc], Display = [.. activeModsShow] };
    }

    internal static double CalculateAccuracy(HitsResult hits, int mode)
    {
      double numerator;
      double denominator;
      switch (mode)
      {
        case 0:
          numerator = 6 * hits.Hit300 + 2 * hits.Hit100 + hits.Hit50;
          denominator = 6 * (hits.Hit50 + hits.Hit100 + hits.Hit300 + hits.HitMiss);
          break;
        case 1:
          numerator = 2 * hits.Hit300 + hits.Hit100;
          denominator = 2 * (hits.Hit300 + hits.Hit100 + hits.HitMiss);
          break;
        case 2:
          numerator = hits.Hit300 + hits.Hit100 + hits.Hit50;
          denominator = hits.Hit300 + hits.Hit100 + hits.Hit50 + hits.HitKatu + hits.HitMiss;
          break;
        case 3:
          numerator =
            6 * hits.HitGeki + 6 * hits.Hit300 + 4 * hits.HitKatu + 2 * hits.Hit100 + hits.Hit50;
          denominator =
            6 * (hits.Hit50 + hits.Hit100 + hits.Hit300 + hits.HitMiss + hits.HitGeki + hits.HitKatu);
          break;
        default:
          throw new ArgumentException("Invalid mode provided.");
      }

      return denominator == 0 ? 0 : 100 * numerator / denominator;
    }

    internal static string? FindUserConfigPath(string osuFolderDirectory)
    {
      if (string.IsNullOrWhiteSpace(osuFolderDirectory) || !Directory.Exists(osuFolderDirectory))
        return null;

      var exact = Path.Combine(osuFolderDirectory, $"osu!.{Environment.UserName}.cfg");
      if (File.Exists(exact))
        return exact;

      try
      {
        return Directory
          .EnumerateFiles(osuFolderDirectory, "osu!.*.cfg", SearchOption.TopDirectoryOnly)
          .OrderByDescending(File.GetLastWriteTimeUtc)
          .FirstOrDefault();
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }

    internal static string GetSongsFolderLocation(string osuFolderDirectory, string customSongsFolder)
    {
      string? file = FindUserConfigPath(osuFolderDirectory);
      if (file == null)
      {
        return string.IsNullOrEmpty(customSongsFolder)
          ? Path.Combine(osuFolderDirectory, "Songs")
          : customSongsFolder;
      }
      foreach (string readLine in File.ReadLines(file))
      {
        if (!readLine.StartsWith("BeatmapDirectory"))
          continue;
        int separator = readLine.IndexOf('=');
        if (separator < 0)
          continue;
        string path = readLine[(separator + 1)..].Trim();
        if (path == "Songs")
          return Path.Combine(osuFolderDirectory, "Songs");
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path, osuFolderDirectory);
      }
      return string.IsNullOrEmpty(customSongsFolder)
        ? Path.Combine(osuFolderDirectory, "Songs")
        : customSongsFolder;
    }
  }
}
