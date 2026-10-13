using System.IO;
using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.Key
{

  internal sealed class KeyOverlayInputService(
    RawInputService rawInput,
    Func<string> osuDirectory,
    Func<bool> isPlaying
  )
  {
    private readonly KeyLayoutResolver _layouts = new(new OsuKeyConfigReader(osuDirectory));

    private readonly object _liveTransitionLock = new();
    private readonly List<RawInputService.KeyTransition> _rawTransitionBuffer = [];
    private LaneBinding[] _liveTransitionBindings = [];
    private int[] _liveTransitionHoldCount = [];
    private bool[] _liveTransitionPressed = [];
    private KeyOverlaySnapshot _cachedLiveSnapshot = KeyOverlaySnapshot.Empty;

    internal KeyOverlaySnapshot DrainKeyOverlayUpdate(
      int gamemode,
      int? maniaKeyCount,
      List<KeyOverlayTransition> transitions
    )
    {
      var layout = _layouts.Resolve(gamemode, gamemode == OsuMode.Mania ? maniaKeyCount : null, isPlaying());
      if (layout.Labels.Length == 0)
      {
        DiscardRawInputTransitions();
        return KeyOverlaySnapshot.Empty;
      }

      return DrainLiveKeyOverlaySnapshot(layout, transitions);
    }

    private void DiscardRawInputTransitions()
    {
      lock (_liveTransitionLock)
      {
        _rawTransitionBuffer.Clear();
        rawInput.DrainTransitions(_rawTransitionBuffer);
      }
    }

    private void EnsureLiveTransitionState(ResolvedKeyLayout layout)
    {
      if (ReferenceEquals(_liveTransitionBindings, layout.Bindings))
        return;
      _liveTransitionBindings = layout.Bindings;
      _liveTransitionHoldCount = new int[layout.Bindings.Length];
      _liveTransitionPressed = new bool[layout.Bindings.Length];
      _cachedLiveSnapshot = layout.BlankSnapshot;

      var activeKeys = new List<Keys>(layout.Bindings.Length * 2);
      foreach (var b in layout.Bindings)
      {
        activeKeys.Add(b.Key);
        activeKeys.Add(b.MouseFallback);
      }
      rawInput.SetActiveKeys(activeKeys);
    }

    private KeyOverlaySnapshot DrainLiveKeyOverlaySnapshot(ResolvedKeyLayout layout, List<KeyOverlayTransition> transitions)
    {
      lock (_liveTransitionLock)
      {
        EnsureLiveTransitionState(layout);

        _rawTransitionBuffer.Clear();
        rawInput.DrainTransitions(_rawTransitionBuffer);

        var bindings = _liveTransitionBindings;
        var holdCount = _liveTransitionHoldCount;
        var pressed = _liveTransitionPressed;

        foreach (var raw in _rawTransitionBuffer)
        {
          for (var lane = 0; lane < bindings.Length; lane++)
          {
            var binding = bindings[lane];
            if (raw.Key != binding.Key && raw.Key != binding.MouseFallback)
              continue;

            holdCount[lane] = Math.Max(0, holdCount[lane] + (raw.IsDown ? 1 : -1));
            var nowPressed = holdCount[lane] > 0;
            if (nowPressed == pressed[lane])
              continue;

            pressed[lane] = nowPressed;
            transitions.Add(new KeyOverlayTransition(lane, nowPressed, raw.TimestampTicks));
          }
        }

        if (transitions.Count == 0 && _cachedLiveSnapshot.Keys.Length == layout.Labels.Length)
          return _cachedLiveSnapshot;

        var keys = new KeyOverlayKeyState[layout.Labels.Length];
        for (var i = 0; i < keys.Length; i++)
          keys[i] = new KeyOverlayKeyState(layout.Labels[i], pressed[i], layout.Roles?[i] ?? KeyOverlayRole.Normal);
        _cachedLiveSnapshot = new KeyOverlaySnapshot(keys);
        return _cachedLiveSnapshot;
      }
    }
  }
}
