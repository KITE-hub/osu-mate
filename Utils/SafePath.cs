using System.IO;
using System.Linq;

namespace OsuMate.Utils
{

  internal static class SafePath
  {
    internal static string? CombineWithin(string root, params string[] parts)
    {
      if (string.IsNullOrWhiteSpace(root))
        return null;

      foreach (var part in parts)
      {
        if (string.IsNullOrWhiteSpace(part) || Path.IsPathRooted(part))
          return null;
      }

      var rootFull =
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
      var full = Path.GetFullPath(Path.Combine(parts.Prepend(root).ToArray()));
      return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
  }
}
