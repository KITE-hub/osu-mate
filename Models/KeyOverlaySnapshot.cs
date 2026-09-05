namespace OsuMate.Models
{
  internal sealed record KeyOverlayKeyState(string Label, bool IsPressed, BeatmapNoteType Role = BeatmapNoteType.Normal);

  internal sealed record KeyOverlaySnapshot(KeyOverlayKeyState[] Keys)
  {
    internal static KeyOverlaySnapshot Empty { get; } = new([]);

    public bool Equals(KeyOverlaySnapshot? other)
    {
      if (other is null)
        return false;
      if (ReferenceEquals(this, other))
        return true;
      if (Keys.Length != other.Keys.Length)
        return false;
      for (var i = 0; i < Keys.Length; i++)
      {
        if (!Keys[i].Equals(other.Keys[i]))
          return false;
      }
      return true;
    }

    public override int GetHashCode()
    {
      var hash = new HashCode();
      foreach (var key in Keys)
        hash.Add(key);
      return hash.ToHashCode();
    }
  }
}
