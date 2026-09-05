namespace OsuMate.Models
{
  internal readonly record struct BeatmapNoteTransition(int LaneIndex, bool IsPressed, long TimestampTicks, BeatmapNoteType NoteType);
}
