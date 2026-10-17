using OsuMate.Models;

namespace OsuMate.Services.Key
{

  internal sealed class ResolvedKeyLayout
  {
    public static readonly ResolvedKeyLayout Empty = new([], []);

    public string[] Labels { get; }
    public LaneBinding[] Bindings { get; }
    public KeyOverlayRole[]? Roles { get; }
    public KeyOverlaySnapshot BlankSnapshot { get; }

    public ResolvedKeyLayout(
      string[] labels,
      LaneBinding[] bindings,
      KeyOverlayRole[]? roles = null
    )
    {
      Labels = labels;
      Bindings = bindings;
      Roles = roles;
      if (labels.Length == 0)
      {
        BlankSnapshot = KeyOverlaySnapshot.Empty;
        return;
      }
      var keys = new KeyOverlayKeyState[labels.Length];
      for (var i = 0; i < labels.Length; i++)
        keys[i] = new KeyOverlayKeyState(labels[i], false, roles?[i] ?? KeyOverlayRole.Normal);
      BlankSnapshot = new KeyOverlaySnapshot(keys);
    }
  }
}
