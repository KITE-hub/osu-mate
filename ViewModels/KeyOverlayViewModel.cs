using System.Collections.Concurrent;
using System.Threading;
using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed class KeyOverlayViewModel
  {
    private readonly ConcurrentQueue<KeyOverlayTransition> _transitions = new();
    private KeyOverlaySnapshot _layout = KeyOverlaySnapshot.Empty;

    internal KeyOverlaySnapshot Layout => Volatile.Read(ref _layout);

    internal void Publish(KeyOverlaySnapshot layout, List<KeyOverlayTransition> transitions)
    {
      Volatile.Write(ref _layout, layout);
      foreach (var transition in transitions)
        _transitions.Enqueue(transition);
    }

    internal void DrainTransitions(List<KeyOverlayTransition> destination)
    {
      while (_transitions.TryDequeue(out var transition))
        destination.Add(transition);
    }
  }
}
