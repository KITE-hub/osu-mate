using System.Windows.Forms;

namespace OsuMate.Services.Key
{

  internal sealed class KeyConfigSnapshot(
    IReadOnlyDictionary<OsuBinding, Keys> bindings,
    IReadOnlyDictionary<int, Keys[]> maniaLayouts
  )
  {
    internal static KeyConfigSnapshot Empty { get; } =
      new(new Dictionary<OsuBinding, Keys>(), new Dictionary<int, Keys[]>());

    internal IReadOnlyDictionary<OsuBinding, Keys> Bindings { get; } = bindings;

    internal IReadOnlyDictionary<int, Keys[]> ManiaLayouts { get; } = maniaLayouts;

    internal Keys GetBinding(OsuBinding binding, Keys fallback) =>
      Bindings.TryGetValue(binding, out var key) ? key : fallback;

    internal Keys[]? GetManiaLayout(int keyCount) =>
      ManiaLayouts.TryGetValue(keyCount, out var keys) ? keys : null;

    internal bool ContentEquals(KeyConfigSnapshot other) =>
      Bindings.Count == other.Bindings.Count
      && ManiaLayouts.Count == other.ManiaLayouts.Count
      && Bindings.All(b => other.Bindings.TryGetValue(b.Key, out var key) && key == b.Value)
      && ManiaLayouts.All(m => other.ManiaLayouts.TryGetValue(m.Key, out var keys) && m.Value.SequenceEqual(keys));
  }
}
