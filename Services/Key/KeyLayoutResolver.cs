using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.Key
{

  internal sealed class KeyLayoutResolver(IOsuKeyConfigProvider config)
  {
    private readonly record struct LaneKeySpec(
      OsuBinding Binding,
      Keys Default,
      Keys MouseFallback,
      KeyOverlayRole Role
    );

    private static readonly LaneKeySpec[] StandardSpecs =
    [
      new(OsuBinding.OsuLeft, Keys.Z, Keys.LButton, KeyOverlayRole.Normal),
      new(OsuBinding.OsuRight, Keys.X, Keys.RButton, KeyOverlayRole.Normal),
    ];

    private static readonly LaneKeySpec[] TaikoSpecs =
    [
      new(OsuBinding.TaikoInnerLeft, Keys.X, Keys.None, KeyOverlayRole.TaikoDon),
      new(OsuBinding.TaikoInnerRight, Keys.C, Keys.None, KeyOverlayRole.TaikoDon),
      new(OsuBinding.TaikoOuterLeft, Keys.Z, Keys.None, KeyOverlayRole.TaikoKat),
      new(OsuBinding.TaikoOuterRight, Keys.V, Keys.None, KeyOverlayRole.TaikoKat),
    ];

    private static readonly LaneKeySpec[] CatchSpecs =
    [
      new(OsuBinding.FruitsDash, Keys.LShiftKey, Keys.None, KeyOverlayRole.Normal),
      new(OsuBinding.FruitsLeft, Keys.Left, Keys.None, KeyOverlayRole.Normal),
      new(OsuBinding.FruitsRight, Keys.Right, Keys.None, KeyOverlayRole.Normal),
    ];

    private static readonly Keys[] FallbackManiaKeys =
    [
      Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
      Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O,
    ];

    private readonly object _layoutLock = new();
    private ResolvedKeyLayout _resolvedLayout = ResolvedKeyLayout.Empty;
    private int _resolvedGamemode = int.MinValue;
    private int _resolvedManiaKeyCount = int.MinValue;

    internal ResolvedKeyLayout Resolve(int gamemode, int? maniaKeyCount, bool isActiveSession)
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
        _resolvedLayout = BuildLayout(config.Current, gamemode, maniaKeyCount);
        return _resolvedLayout;
      }
    }

    private static ResolvedKeyLayout BuildLayout(
      KeyConfigSnapshot snapshot,
      int gamemode,
      int? maniaKeyCount
    ) =>
      gamemode switch
      {
        OsuMode.Osu => BuildLayoutFromSpecs(snapshot, StandardSpecs, reorderByPhysicalPosition: false),
        OsuMode.Taiko => BuildLayoutFromSpecs(snapshot, TaikoSpecs, reorderByPhysicalPosition: true),
        OsuMode.Catch => BuildLayoutFromSpecs(snapshot, CatchSpecs, reorderByPhysicalPosition: false),
        OsuMode.Mania when maniaKeyCount is >= OsuMode.ManiaMinKeyCount and <= OsuMode.ManiaMaxKeyCount =>
          BuildManiaLayout(snapshot, maniaKeyCount.Value),
        _ => ResolvedKeyLayout.Empty,
      };

    private static ResolvedKeyLayout BuildLayoutFromSpecs(
      KeyConfigSnapshot snapshot,
      LaneKeySpec[] specs,
      bool reorderByPhysicalPosition
    )
    {
      var resolvedKeys = new Keys[specs.Length];
      for (var i = 0; i < specs.Length; i++)
        resolvedKeys[i] = snapshot.GetBinding(specs[i].Binding, specs[i].Default);

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
        var key = resolvedKeys[order[i]];
        labels[i] = OsuKeyNames.ToName(key);
        bindings[i] = new LaneBinding(key, spec.MouseFallback);
        roles[i] = spec.Role;
      }

      return new ResolvedKeyLayout(labels, bindings, roles);
    }

    private static ResolvedKeyLayout BuildManiaLayout(KeyConfigSnapshot snapshot, int keyCount)
    {
      var keys = snapshot.GetManiaLayout(keyCount);
      if (keys is null || keys.Length < keyCount)
        keys = FallbackManiaKeys[..Math.Min(keyCount, FallbackManiaKeys.Length)];
      else if (keys.Length > keyCount)
        keys = keys[..keyCount];

      var labels = new string[keys.Length];
      var bindings = new LaneBinding[keys.Length];
      for (var i = 0; i < keys.Length; i++)
      {
        labels[i] = OsuKeyNames.ToName(keys[i]);
        bindings[i] = new LaneBinding(keys[i], Keys.None);
      }

      return new ResolvedKeyLayout(labels, bindings);
    }
  }
}
