namespace OsuMate.Models;

public enum BeatmapNoteType : byte
{
  Normal,
  Hold,
  TaikoDon,
  TaikoKat
}

public readonly record struct BeatmapOverlayNote(
  double StartTime,
  double EndTime,
  int Lane,
  BeatmapNoteType Type
);
