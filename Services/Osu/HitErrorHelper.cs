using System;
using System.Collections.Generic;

namespace OsuMate.Services.Osu;

internal static class HitErrorHelper
{
  internal static double ToModified(int rawHitError, double speedMultiplier) =>
    rawHitError * speedMultiplier;

  internal static int ToModifiedRounded(int rawHitError, double speedMultiplier) =>
    (int)Math.Round(ToModified(rawHitError, speedMultiplier), MidpointRounding.AwayFromZero);

  internal static bool IsDiscontinuous(
    IReadOnlyList<int> hitErrors,
    int previousCount,
    int previousLastValue
  ) =>
    hitErrors.Count < previousCount
    || (previousCount > 0 && hitErrors[previousCount - 1] != previousLastValue);
}
