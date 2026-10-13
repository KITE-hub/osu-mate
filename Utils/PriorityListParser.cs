namespace OsuMate.Utils
{

  internal static class PriorityListParser
  {
    internal static List<TItem> Resolve<TItem>(
      string? priority,
      string fallbackPriority,
      IReadOnlyList<(int id, string label)> definitions,
      bool missingItemEnabled,
      Func<int, string, bool, TItem> create
    )
    {
      var definitionsById = definitions.ToDictionary(d => d.id, d => d.label);
      var seen = new HashSet<int>();
      var items = new List<TItem>(definitions.Count);

      foreach (var token in (priority ?? fallbackPriority).Split('/', StringSplitOptions.RemoveEmptyEntries))
      {
        bool enabled = !token.StartsWith('-');
        string raw = enabled ? token : token[1..];
        if (!int.TryParse(raw, out int id) || id <= 0)
          continue;
        if (!definitionsById.TryGetValue(id, out var label) || !seen.Add(id))
          continue;

        items.Add(create(id, label, enabled));
      }

      foreach (var (id, label) in definitions)
      {
        if (seen.Add(id))
          items.Add(create(id, label, missingItemEnabled));
      }

      return items;
    }

    internal static string ToPriorityString(IEnumerable<(int id, bool enabled)> items) =>
      string.Join("/", items.Select(i => i.enabled ? i.id.ToString() : $"-{i.id}"));
  }
}
