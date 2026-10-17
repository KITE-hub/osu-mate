using System.Windows.Forms;
using OsuMate.Models;

namespace OsuMate.Services.Key
{

  internal sealed class KeyOverlayInputService(
    RawInputService rawInput,
    IOsuKeyConfigProvider keyConfig,
    Func<bool> isPlaying
  )
  {
    private readonly KeyLayoutResolver _layouts = new(keyConfig);

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
        ReleaseLiveInput();
        return KeyOverlaySnapshot.Empty;
      }

      return DrainLiveKeyOverlaySnapshot(layout, transitions);
    }

    private void ReleaseLiveInput()
    {
      lock (_liveTransitionLock)
      {
        ReleaseLiveTransitionState();
        rawInput.DrainTransitions(_rawTransitionBuffer);
        _rawTransitionBuffer.Clear();
      }
    }

    private void ReleaseLiveTransitionState()
    {
      if (_liveTransitionBindings.Length == 0)
        return;

      _liveTransitionBindings = [];
      _liveTransitionHoldCount = [];
      _liveTransitionPressed = [];
      _cachedLiveSnapshot = KeyOverlaySnapshot.Empty;
      rawInput.SetActiveKeys([]);
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
        var changed = false;

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
            changed = true;
          }
        }

        if (!changed && _cachedLiveSnapshot.Keys.Length == layout.Labels.Length)
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
