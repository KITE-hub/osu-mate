using System.Threading;
using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class KeyOverlayViewModel
  {
    private sealed record VersionedSnapshot(long Version, KeyOverlaySnapshot Snapshot);

    private static readonly VersionedSnapshot Initial = new(0, KeyOverlaySnapshot.Empty);

    private VersionedSnapshot _current = Initial;

    internal KeyOverlaySnapshot Snapshot => Volatile.Read(ref _current).Snapshot;

    internal void Update(KeyOverlaySnapshot snapshot, long version)
    {
      VersionedSnapshot current;
      do
      {
        current = Volatile.Read(ref _current);
        if (version <= current.Version)
          return;
        if (snapshot.Equals(current.Snapshot))
          return;
      }
      while (Interlocked.CompareExchange(ref _current, new VersionedSnapshot(version, snapshot), current) != current);
    }
  }
}
