using System.IO;
using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.Key
{

    internal readonly record struct LaneBinding(Keys Key, Keys MouseFallback);

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

  internal sealed class KeyLayoutResolver(OsuKeyConfigReader config)
  {
    private readonly object _layoutLock = new();
    private ResolvedKeyLayout _resolvedLayout = ResolvedKeyLayout.Empty;
    private int _resolvedGamemode = int.MinValue;
    private int _resolvedManiaKeyCount = int.MinValue;

    internal ResolvedKeyLayout Resolve(int gamemode, int? maniaKeyCount, bool isActiveSession) =>
      ResolveLayout(gamemode, maniaKeyCount, isActiveSession);

    private ResolvedKeyLayout ResolveLayout(
      int gamemode,
      int? maniaKeyCount,
      bool isActiveSession
    )
    {
      var configChanged = config.Refresh(isActiveSession);
      var maniaCount = maniaKeyCount ?? -1;

      lock (_layoutLock)
      {
        if (
          !configChanged
          && _resolvedGamemode == gamemode
          && _resolvedManiaKeyCount == maniaCount
        )
          return _resolvedLayout;

        _resolvedGamemode = gamemode;
        _resolvedManiaKeyCount = maniaCount;
        _resolvedLayout = BuildLayout(gamemode, maniaKeyCount);
        return _resolvedLayout;
      }
    }

    private ResolvedKeyLayout BuildLayout(
      int gamemode,
      int? maniaKeyCount
    ) =>
      gamemode switch
      {
        0 => BuildStandardLayout(),
        1 => BuildTaikoLayout(),
        2 => BuildFixedLayout(
          ("keyFruitsDash", "LeftShift", Keys.None),
          ("keyFruitsLeft", "Left", Keys.None),
          ("keyFruitsRight", "Right", Keys.None)
        ),
        3 when maniaKeyCount is >= 1 and <= 18 => BuildManiaLayout(maniaKeyCount.Value),
        _ => ResolvedKeyLayout.Empty,
      };

    private ResolvedKeyLayout BuildStandardLayout()
    {
      var specs = new TaikoKeySpec[]
      {
        new("keyOsuLeft", "Z", Keys.LButton, KeyOverlayRole.Normal),
        new("keyOsuRight", "X", Keys.RButton, KeyOverlayRole.Normal)
      };
      return BuildLayoutFromSpecs(specs, reorderByPhysicalPosition: false);
    }

    private ResolvedKeyLayout BuildTaikoLayout()
    {
      var specs = new TaikoKeySpec[]
      {
        new("keyTaikoInnerLeft", "X", Keys.None, KeyOverlayRole.TaikoDon),
        new("keyTaikoInnerRight", "C", Keys.None, KeyOverlayRole.TaikoDon),
        new("keyTaikoOuterLeft", "Z", Keys.None, KeyOverlayRole.TaikoKat),
        new("keyTaikoOuterRight", "V", Keys.None, KeyOverlayRole.TaikoKat)
      };
      return BuildLayoutFromSpecs(specs, reorderByPhysicalPosition: true);
    }

    private readonly record struct TaikoKeySpec(
      string ConfigKey,
      string Fallback,
      Keys MouseFallback,
      KeyOverlayRole Role
    );

    private ResolvedKeyLayout BuildLayoutFromSpecs(
      TaikoKeySpec[] specs,
      bool reorderByPhysicalPosition
    )
    {
      var resolvedNames = new string[specs.Length];
      var resolvedKeys = new Keys[specs.Length];
      for (var i = 0; i < specs.Length; i++)
      {
        resolvedNames[i] = config.GetConfiguredKeyName(specs[i].ConfigKey, specs[i].Fallback);
        resolvedKeys[i] = OsuKeyConfigReader.TryParseKey(resolvedNames[i], out var parsedKey) ? parsedKey : Keys.None;
      }

      var order = new int[specs.Length];
      for (var i = 0; i < order.Length; i++)
        order[i] = i;

      if (reorderByPhysicalPosition)
      {
        Array.Sort(order, (a, b) =>
        {
          var positionA = KeyboardPhysicalLayoutUtils.GetPhysicalPosition(resolvedKeys[a]);
          var positionB = KeyboardPhysicalLayoutUtils.GetPhysicalPosition(resolvedKeys[b]);
          return positionA != positionB ? positionA.CompareTo(positionB) : a.CompareTo(b);
        });
      }

      var labels = new string[specs.Length];
      var bindings = new LaneBinding[specs.Length];
      var roles = new KeyOverlayRole[specs.Length];

      for (var i = 0; i < order.Length; i++)
      {
        var spec = specs[order[i]];
        labels[i] = resolvedNames[order[i]];
        bindings[i] = new LaneBinding(resolvedKeys[order[i]], spec.MouseFallback);
        roles[i] = spec.Role;
      }

      return new ResolvedKeyLayout(labels, bindings, roles);
    }

    private ResolvedKeyLayout BuildFixedLayout(params (string ConfigKey, string Fallback, Keys MouseFallback)[] specs)
    {
      var labels = new string[specs.Length];
      var bindings = new LaneBinding[specs.Length];
      for (var i = 0; i < specs.Length; i++)
      {
        var name = config.GetConfiguredKeyName(specs[i].ConfigKey, specs[i].Fallback);
        labels[i] = name;
        bindings[i] = new LaneBinding(
          OsuKeyConfigReader.TryParseKey(name, out var key) ? key : Keys.None,
          specs[i].MouseFallback
        );
      }
      return new ResolvedKeyLayout(labels, bindings);
    }

    private ResolvedKeyLayout BuildManiaLayout(int keyCount)
    {
      var keys = config.ReadManiaLayout(keyCount);
      if (keys.Length < keyCount)
        keys = GetFallbackManiaLayout(keyCount);
      else if (keys.Length > keyCount)
        keys = keys[..keyCount];

      var bindings = new LaneBinding[keyCount];
      for (var i = 0; i < keyCount; i++)
        bindings[i] = new LaneBinding(OsuKeyConfigReader.TryParseKey(keys[i], out var key) ? key : Keys.None, Keys.None);

      return new ResolvedKeyLayout(keys, bindings);
    }

    private static string[] GetFallbackManiaLayout(int keyCount)
    {
      var fallback = new[]
      {
        "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O"
      };
      return fallback[..Math.Min(keyCount, fallback.Length)];
    }
  }
}
