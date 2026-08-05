using System.IO;
using System.Windows;
using osu.Game.IO;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.Osu;

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
        { 1073741824, "MR" }
    };

    internal static string ConvertHits(int mode, HitsResult hits)
    {
        return mode switch
        {
            0 => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
            1 => $"{hits.Hit300}/{hits.Hit100}/{hits.HitMiss}",
            2 => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
            3 => $"{hits.HitGeki}/{hits.Hit300}/{hits.HitKatu}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}",
            _ => $"{hits.Hit300}/{hits.Hit100}/{hits.Hit50}/{hits.HitMiss}"
        };
    }

    internal static Mods ParseMods(int mods)
    {
        List<string> activeModsCalc = [];
        List<string> activeModsShow = [];

        for (int i = 0; i < 32; i++)
        {
            int bit = 1 << i;
            if ((mods & bit) != bit) continue;
            if (osu_mods.TryGetValue(bit, out var modStr))
            {
                activeModsCalc.Add(modStr.ToLower());
                activeModsShow.Add(modStr);
            }
        }

        if (activeModsCalc.Contains("nc") && activeModsCalc.Contains("dt")) activeModsCalc.Remove("nc");
        if (activeModsShow.Contains("NC") && activeModsShow.Contains("DT")) activeModsShow.Remove("DT");
        if (activeModsShow.Count == 0) activeModsShow.Add("NM");

        return new Mods()
        {
            Calculation = [.. activeModsCalc],
            Display = [.. activeModsShow]
        };
    }

    internal static double CalculateAccuracy(HitsResult hits, int mode)
    {
        return mode switch
        {
            0 => (double)(100 * ((6 * hits.Hit300) + (2 * hits.Hit100) + hits.Hit50)) / (6 * (hits.Hit50 + hits.Hit100 + hits.Hit300 + hits.HitMiss)),
            1 => (double)(100 * ((2 * hits.Hit300) + hits.Hit100)) / (2 * (hits.Hit300 + hits.Hit100 + hits.HitMiss)),
            2 => (double)(100 * (hits.Hit300 + hits.Hit100 + hits.Hit50)) / (hits.Hit300 + hits.Hit100 + hits.Hit50 + hits.HitKatu + hits.HitMiss),
            3 => (double)(100 * ((6 * hits.HitGeki) + (6 * hits.Hit300) + (4 * hits.HitKatu) + (2 * hits.Hit100) + hits.Hit50)) / (6 * (hits.Hit50 + hits.Hit100 + hits.Hit300 + hits.HitMiss + hits.HitGeki + hits.HitKatu)),
            _ => throw new ArgumentException("Invalid mode provided.")
        };
    }

    internal static int GetMapMode(string file)
    {
        using var stream = File.OpenRead(file);
        using var reader = new LineBufferedReader(stream);
        int count = 0;
        while (reader.ReadLine() is { } line)
        {
            if (count > 20) return 0;
            if (line.StartsWith("Mode")) return int.Parse(line.Split(':')[1].Trim());
            count++;
        }

        return -1;
    }

    internal static string GetSongsFolderLocation(string osuFolderDirectory, string customSongsFolder)
    {
        string userName = Environment.UserName;
        string file = Path.Combine(osuFolderDirectory, $"osu!.{userName}.cfg");
        if (!File.Exists(file))
        {
            System.Windows.MessageBox.Show(
                "Could not auto-detect Songs folder because osu!.Username.cfg was not found.\nFalling back to SongsFolder in config (or the default Songs folder if not set).",
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return string.IsNullOrEmpty(customSongsFolder)
                ? Path.Combine(osuFolderDirectory, "Songs")
                : customSongsFolder;
        }
        foreach (string readLine in File.ReadLines(file))
        {
            if (!readLine.StartsWith("BeatmapDirectory")) continue;
            string path = readLine.Split('=')[1].Trim(' ');
            return path == "Songs" ? Path.Combine(osuFolderDirectory, "Songs") : path;
        }
        FormUtils.ShowErrorMessageBox("Could not auto-detect Songs folder because osu!.Username.cfg was not found.\nFalling back to SongsFolder in config (or the default Songs folder if not set).");
        return string.IsNullOrEmpty(customSongsFolder) ? Path.Combine(osuFolderDirectory, "Songs") : customSongsFolder;
    }
}
