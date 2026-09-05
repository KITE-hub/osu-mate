using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Taiko.Objects;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.Key;

public sealed class BeatmapOverlayService
{
  private static readonly BeatmapOverlayNote[] EmptyNotes = [];
  private static readonly TimeSpan ReloadInterval = TimeSpan.FromMilliseconds(500);

  private readonly object _reloadLock = new();
  private bool _isReloading;
  private DateTime _lastReloadAttemptUtc = DateTime.MinValue;

  private BeatmapOverlayNote[] _notes = EmptyNotes;
  private string _loadedPath = string.Empty;
  private string _loadedMd5 = string.Empty;
  private int _loadedMode = -1;
  private int? _loadedManiaKeyCount;

  public BeatmapOverlayNote[] CurrentNotes => Volatile.Read(ref _notes);

  public void UpdateCurrentBeatmap(string beatmapPath, string beatmapMd5, int mode, int? maniaKeyCount)
  {
    if (
      beatmapPath == _loadedPath
      && beatmapMd5 == _loadedMd5
      && mode == _loadedMode
      && maniaKeyCount == _loadedManiaKeyCount
    )
      return;

    lock (_reloadLock)
    {
      if (_isReloading || DateTime.UtcNow - _lastReloadAttemptUtc < ReloadInterval)
        return;
      _isReloading = true;
      _lastReloadAttemptUtc = DateTime.UtcNow;
    }

    Task.Run(() => ReloadNotes(beatmapPath, beatmapMd5, mode, maniaKeyCount));
  }

  private void ReloadNotes(string beatmapPath, string beatmapMd5, int mode, int? maniaKeyCount)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(beatmapPath) || !File.Exists(beatmapPath) || mode is not (0 or 1 or 3))
      {
        Volatile.Write(ref _notes, EmptyNotes);
        _loadedPath = beatmapPath;
        _loadedMd5 = beatmapMd5;
        _loadedMode = mode;
        _loadedManiaKeyCount = maniaKeyCount;
        return;
      }

      var workingBeatmap = ProcessorWorkingBeatmap.FromFile(beatmapPath);
      var ruleset = RulesetHelper.GetRuleset(mode);
      var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<Mod>());
      var rawObjects = playable.HitObjects;

      var result = new List<BeatmapOverlayNote>(rawObjects.Count);

      switch (mode)
      {
        case 0:
          foreach (var obj in rawObjects)
          {
            if (obj is HitCircle circle)
            {
              result.Add(new BeatmapOverlayNote(circle.StartTime, circle.StartTime, 0, BeatmapNoteType.Normal));
            }
            else if (obj is Slider slider)
            {
              result.Add(new BeatmapOverlayNote(slider.StartTime, slider.GetEndTime(), 0, BeatmapNoteType.Hold));
            }
          }
          break;

        case 1:
          foreach (var obj in rawObjects)
          {
            if (obj is Hit hit)
            {
              var type = hit.Type == HitType.Centre ? BeatmapNoteType.TaikoDon : BeatmapNoteType.TaikoKat;
              result.Add(new BeatmapOverlayNote(hit.StartTime, hit.StartTime, 0, type));
            }
          }
          break;

        case 3:
          foreach (var obj in rawObjects)
          {
            if (obj is Note note)
            {
              result.Add(new BeatmapOverlayNote(note.StartTime, note.StartTime, note.Column, BeatmapNoteType.Normal));
            }
            else if (obj is HoldNote hold)
            {
              result.Add(new BeatmapOverlayNote(hold.StartTime, hold.GetEndTime(), hold.Column, BeatmapNoteType.Hold));
            }
          }
          break;
      }

      var sorted = result.OrderBy(n => n.StartTime).ToArray();
      Volatile.Write(ref _notes, sorted);
      _loadedPath = beatmapPath;
      _loadedMd5 = beatmapMd5;
      _loadedMode = mode;
      _loadedManiaKeyCount = maniaKeyCount;
    }
    catch (Exception e)
    {
      Volatile.Write(ref _notes, EmptyNotes);
      LogUtils.DebugLogger($"BeatmapOverlayService.ReloadNotes failed: {e.Message}", true);
    }
    finally
    {
      lock (_reloadLock)
        _isReloading = false;
    }
  }
}
