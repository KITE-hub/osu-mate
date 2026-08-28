using System.Globalization;
using System.Numerics;

namespace OsuMate.Services.Trainer
{
  internal static class RandomModApplier
  {
    private const float PlayfieldWidth = 512f;
    private const float PlayfieldHeight = 384f;
    private static readonly Vector2 PlayfieldCentre = new(PlayfieldWidth / 2f, PlayfieldHeight / 2f);

    public static bool IsSupported(int mode) => mode != 2;

    public static List<string> Apply(
      IReadOnlyList<string> hitObjectLines,
      int mode,
      decimal circleSize,
      Random random
    )
    {
      return mode switch
      {
        1 => ApplyTaiko(hitObjectLines, random),
        2 => hitObjectLines.ToList(),
        3 => ApplyMania(hitObjectLines, circleSize, random),
        _ => ApplyStandard(hitObjectLines, circleSize, random),
      };
    }

    private static float ComputeCircleRadius(decimal circleSize)
    {
      double cs = Math.Clamp((double)circleSize, 0.0, 11.0);
      return (float)(54.4 - 4.48 * cs);
    }

    private sealed class ParsedHitObject
    {
      public required string[] Parts { get; init; }
      public required Vector2 Position { get; init; }
      public bool IsSpinner { get; init; }
      public bool IsSlider { get; init; }
      public string CurveTypeLetter { get; init; } = "";
      public List<Vector2> CurvePoints { get; init; } = [];
      public int Slides { get; init; } = 1;
    }

    private static ParsedHitObject? ParseStandardObject(string rawLine)
    {
      var line = rawLine.Trim();
      if (line.Length == 0 || line.StartsWith("//"))
        return null;

      var parts = line.Split(',');
      if (parts.Length < 4)
        return null;
      if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
        return null;
      if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        return null;
      if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
        return null;

      bool isSpinner = (type & 8) != 0;
      bool isSlider = (type & 2) != 0;

      var curvePoints = new List<Vector2>();
      var curveTypeLetter = "";
      int slides = 1;

      if (isSlider && parts.Length > 6)
      {
        var curveSegments = parts[5].Split('|');
        if (curveSegments.Length > 0)
          curveTypeLetter = curveSegments[0];

        for (int i = 1; i < curveSegments.Length; i++)
        {
          var xy = curveSegments[i].Split(':');
          if (
            xy.Length == 2
            && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float cx)
            && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float cy)
          )
            curvePoints.Add(new Vector2(cx, cy));
        }

        if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out slides))
          slides = 1;
      }

      return new ParsedHitObject
      {
        Parts = parts,
        Position = new Vector2(x, y),
        IsSpinner = isSpinner,
        IsSlider = isSlider,
        CurveTypeLetter = curveTypeLetter,
        CurvePoints = curvePoints,
        Slides = slides,
      };
    }

    private static Vector2 TailOffset(ParsedHitObject obj)
    {
      if (!obj.IsSlider || obj.CurvePoints.Count == 0 || obj.Slides % 2 == 0)
        return Vector2.Zero;
      return obj.CurvePoints[^1] - obj.Position;
    }

    private static Vector2 ClampToPlayfield(Vector2 position, float radius)
    {
      float rx = Math.Min(radius, PlayfieldWidth / 2f - 1f);
      float ry = Math.Min(radius, PlayfieldHeight / 2f - 1f);
      float clampedX = Math.Clamp(position.X, rx, PlayfieldWidth - rx);
      float clampedY = Math.Clamp(position.Y, ry, PlayfieldHeight - ry);
      return new Vector2(clampedX, clampedY);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
      float cos = MathF.Cos(radians);
      float sin = MathF.Sin(radians);
      return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static Vector2 ApplyFlipRotation(Vector2 offset, bool flip, float rotation) =>
      Rotate(flip ? new Vector2(-offset.X, offset.Y) : offset, rotation);

    private static List<Vector2> TransformOffsets(
      Vector2 headPosition,
      IReadOnlyList<Vector2> offsets,
      bool flip,
      float rotation
    )
    {
      var points = new List<Vector2>(offsets.Count);
      foreach (var offset in offsets)
        points.Add(headPosition + ApplyFlipRotation(offset, flip, rotation));
      return points;
    }

    private static float ComputeAxisOverflow(float value, float lowerBound, float upperBound)
    {
      if (value < lowerBound)
        return lowerBound - value;
      if (value > upperBound)
        return value - upperBound;
      return 0f;
    }

    private static float ComputeRotatedOverflow(
      Vector2 headPosition,
      IReadOnlyList<Vector2> offsets,
      bool flip,
      float rotation,
      float rx,
      float ry
    )
    {
      float overflow = 0f;
      foreach (var offset in offsets)
      {
        var point = headPosition + ApplyFlipRotation(offset, flip, rotation);
        overflow += ComputeAxisOverflow(point.X, rx, PlayfieldWidth - rx);
        overflow += ComputeAxisOverflow(point.Y, ry, PlayfieldHeight - ry);
      }
      return overflow;
    }

    private static float PointToSegmentDistance(Vector2 point, Vector2 segStart, Vector2 segEnd)
    {
      var segment = segEnd - segStart;
      float lengthSquared = segment.LengthSquared();
      if (lengthSquared < 1e-6f)
        return Vector2.Distance(point, segStart);
      float t = Math.Clamp(Vector2.Dot(point - segStart, segment) / lengthSquared, 0f, 1f);
      var projection = segStart + segment * t;
      return Vector2.Distance(point, projection);
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
      return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
      float d1 = Cross(p3, p4, p1);
      float d2 = Cross(p3, p4, p2);
      float d3 = Cross(p1, p2, p3);
      float d4 = Cross(p1, p2, p4);

      return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float SegmentToSegmentDistance(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
      if (SegmentsIntersect(p1, p2, p3, p4))
        return 0f;

      float d1 = PointToSegmentDistance(p1, p3, p4);
      float d2 = PointToSegmentDistance(p2, p3, p4);
      float d3 = PointToSegmentDistance(p3, p1, p2);
      float d4 = PointToSegmentDistance(p4, p1, p2);

      return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
    }

    private static float MinDistanceBetweenPointAndPath(Vector2 point, IReadOnlyList<Vector2> path)
    {
      if (path.Count == 1)
        return Vector2.Distance(point, path[0]);

      float minDistance = float.MaxValue;
      for (int i = 0; i < path.Count - 1; i++)
        minDistance = Math.Min(minDistance, PointToSegmentDistance(point, path[i], path[i + 1]));
      return minDistance;
    }

    private static float MinDistanceBetweenPaths(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
      if (a.Count == 1)
        return MinDistanceBetweenPointAndPath(a[0], b);
      if (b.Count == 1)
        return MinDistanceBetweenPointAndPath(b[0], a);

      float minDistance = float.MaxValue;
      for (int i = 0; i < a.Count - 1; i++)
      for (int j = 0; j < b.Count - 1; j++)
        minDistance = Math.Min(minDistance, SegmentToSegmentDistance(a[i], a[i + 1], b[j], b[j + 1]));
      return minDistance;
    }

    private static Vector2? Circumcenter(Vector2 a, Vector2 b, Vector2 c)
    {
      float d = 2f * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
      if (MathF.Abs(d) < 1e-6f)
        return null;

      float aSq = a.LengthSquared();
      float bSq = b.LengthSquared();
      float cSq = c.LengthSquared();

      float ux = (aSq * (b.Y - c.Y) + bSq * (c.Y - a.Y) + cSq * (a.Y - b.Y)) / d;
      float uy = (aSq * (c.X - b.X) + bSq * (a.X - c.X) + cSq * (b.X - a.X)) / d;
      return new Vector2(ux, uy);
    }

    private static float NormalizeAnglePositive(float angle)
    {
      const float twoPi = 2f * MathF.PI;
      angle %= twoPi;
      if (angle < 0f)
        angle += twoPi;
      return angle;
    }

    private static List<Vector2> SamplePerfectCurve(Vector2 a, Vector2 b, Vector2 c, int sampleCount)
    {
      var center = Circumcenter(a, b, c);
      if (center is not { } o || Vector2.Distance(o, a) < 1e-3f)
        return new List<Vector2> { a, b, c };

      float radius = Vector2.Distance(o, a);
      float thetaStart = MathF.Atan2(a.Y - o.Y, a.X - o.X);
      float thetaMid = MathF.Atan2(b.Y - o.Y, b.X - o.X);
      float thetaEnd = MathF.Atan2(c.Y - o.Y, c.X - o.X);

      float relMid = NormalizeAnglePositive(thetaMid - thetaStart);
      float relEnd = NormalizeAnglePositive(thetaEnd - thetaStart);
      float sweep = relMid <= relEnd ? relEnd : relEnd - 2f * MathF.PI;

      var points = new List<Vector2>(sampleCount + 1);
      for (int i = 0; i <= sampleCount; i++)
      {
        float t = (float)i / sampleCount;
        float angle = thetaStart + sweep * t;
        points.Add(o + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
      }
      return points;
    }

    private static List<List<Vector2>> SplitBezierSegments(List<Vector2> anchors)
    {
      var segments = new List<List<Vector2>>();
      var current = new List<Vector2>();

      foreach (var anchor in anchors)
      {
        if (current.Count > 0 && Vector2.DistanceSquared(current[^1], anchor) < 1e-6f)
        {
          if (current.Count > 1)
            segments.Add(current);
          current = new List<Vector2> { anchor };
          continue;
        }
        current.Add(anchor);
      }

      if (current.Count > 1)
        segments.Add(current);

      return segments;
    }

    private static Vector2 EvaluateBezier(IReadOnlyList<Vector2> controlPoints, float t)
    {
      var points = controlPoints.ToArray();
      for (int level = 1; level < points.Length; level++)
        for (int i = 0; i < points.Length - level; i++)
          points[i] = Vector2.Lerp(points[i], points[i + 1], t);
      return points[0];
    }

    private static List<Vector2> SampleBezierSegments(List<Vector2> anchors, int totalSamples)
    {
      var segments = SplitBezierSegments(anchors);
      if (segments.Count == 0)
        return anchors;

      var result = new List<Vector2>();
      int samplesPerSegment = Math.Max(1, totalSamples / segments.Count);

      for (int s = 0; s < segments.Count; s++)
      {
        var segment = segments[s];
        int steps = segment.Count == 2 ? 1 : samplesPerSegment;
        int startIndex = s == 0 ? 0 : 1;
        for (int i = startIndex; i <= steps; i++)
          result.Add(EvaluateBezier(segment, (float)i / steps));
      }

      return result;
    }

    private static Vector2 EvaluateCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
      float t2 = t * t;
      float t3 = t2 * t;
      return 0.5f
        * ((2f * p1)
          + (-p0 + p2) * t
          + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
          + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static List<Vector2> SampleCatmullRom(List<Vector2> anchors, int totalSamples)
    {
      int segmentCount = anchors.Count - 1;
      int samplesPerSegment = Math.Max(1, totalSamples / segmentCount);
      var result = new List<Vector2> { anchors[0] };

      for (int i = 0; i < segmentCount; i++)
      {
        var p0 = i > 0 ? anchors[i - 1] : anchors[i];
        var p1 = anchors[i];
        var p2 = anchors[i + 1];
        var p3 = i + 2 < anchors.Count ? anchors[i + 2] : anchors[i + 1];

        for (int step = 1; step <= samplesPerSegment; step++)
          result.Add(EvaluateCatmullRom(p0, p1, p2, p3, (float)step / samplesPerSegment));
      }

      return result;
    }

    private static List<Vector2> ComputeCurvePath(ParsedHitObject obj, int sampleBudget)
    {
      var anchors = new List<Vector2>(obj.CurvePoints.Count + 1) { obj.Position };
      anchors.AddRange(obj.CurvePoints);

      if (anchors.Count < 2)
        return anchors;

      return obj.CurveTypeLetter switch
      {
        "L" => anchors,
        "P" when anchors.Count == 3 => SamplePerfectCurve(anchors[0], anchors[1], anchors[2], sampleBudget),
        "C" => SampleCatmullRom(anchors, sampleBudget),
        _ => SampleBezierSegments(anchors, sampleBudget),
      };
    }

    private static List<Vector2> ComputeCollisionOffsets(ParsedHitObject obj)
    {
      const int sampleBudget = 10;
      return ComputeCurvePath(obj, sampleBudget).Select(p => p - obj.Position).ToList();
    }

    private static Vector2 ChooseTapPosition(
      Vector2 anchor,
      float distance,
      float radius,
      IReadOnlyList<Vector2>? previousShape,
      Random random
    )
    {
      const int angleSamples = 72;

      float rx = Math.Min(radius, PlayfieldWidth / 2f - 1f);
      float ry = Math.Min(radius, PlayfieldHeight / 2f - 1f);
      float minSeparation = 2f * radius;

      var fittingPositions = new List<Vector2>();
      Vector2 bestPosition = anchor;
      float bestPenalty = float.MaxValue;

      for (int s = 0; s < angleSamples; s++)
      {
        float angle = (float)(2.0 * Math.PI * (s + random.NextDouble()) / angleSamples);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var position = anchor + direction * distance;

        float overflow =
          ComputeAxisOverflow(position.X, rx, PlayfieldWidth - rx)
          + ComputeAxisOverflow(position.Y, ry, PlayfieldHeight - ry);

        float overlapDeficit = 0f;
        if (previousShape is { Count: > 0 })
          overlapDeficit = Math.Max(0f, minSeparation - MinDistanceBetweenPointAndPath(position, previousShape));

        if (overflow <= 0f && overlapDeficit <= 0f)
          fittingPositions.Add(position);
        else
        {
          float penalty = overflow + overlapDeficit;
          if (penalty < bestPenalty)
          {
            bestPenalty = penalty;
            bestPosition = position;
          }
        }
      }

      var chosenPosition = fittingPositions.Count > 0
        ? fittingPositions[random.Next(fittingPositions.Count)]
        : bestPosition;

      return ClampToPlayfield(chosenPosition, radius);
    }

    private static float ComputeRotatedOverlapDeficit(
      Vector2 headPosition,
      IReadOnlyList<Vector2> offsets,
      bool flip,
      float rotation,
      IReadOnlyList<Vector2> previousShape,
      float minSeparation
    )
    {
      var bodyPoints = TransformOffsets(headPosition, offsets, flip, rotation);
      float separation = MinDistanceBetweenPaths(bodyPoints, previousShape);
      return Math.Max(0f, minSeparation - separation);
    }

    private static (Vector2 Head, bool Flip, float Rotation) ChooseSliderPlacement(
      Vector2 anchor,
      float distance,
      float radius,
      IReadOnlyList<Vector2> collisionOffsets,
      IReadOnlyList<Vector2>? previousShape,
      Random random
    )
    {
      const int angleSamples = 24;
      const int rotationSamples = 24;

      float rx = Math.Min(radius, PlayfieldWidth / 2f - 1f);
      float ry = Math.Min(radius, PlayfieldHeight / 2f - 1f);
      float minSeparation = 2f * radius;

      float shapeRadius = 0f;
      foreach (var offset in collisionOffsets)
        shapeRadius = Math.Max(shapeRadius, offset.Length());

      float prevShapeRadius = 0f;
      if (previousShape is { Count: > 0 })
        foreach (var point in previousShape)
          prevShapeRadius = Math.Max(prevShapeRadius, Vector2.Distance(point, previousShape[0]));

      var fitting = new List<(Vector2 Head, bool Flip, float Rotation)>();
      Vector2 bestHead = anchor;
      bool bestFlip = false;
      float bestRotation = 0f;
      float bestPenalty = float.MaxValue;

      for (int a = 0; a < angleSamples; a++)
      {
        float angle = (float)(2.0 * Math.PI * (a + random.NextDouble()) / angleSamples);
        var head = anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

        bool skipOverlapCheck =
          previousShape is not { Count: > 0 }
          || Vector2.Distance(head, previousShape[0]) - shapeRadius - prevShapeRadius >= minSeparation;

        foreach (bool flip in new[] { false, true })
        {
          for (int s = 0; s < rotationSamples; s++)
          {
            float rotation = (float)(2.0 * Math.PI * (s + random.NextDouble()) / rotationSamples);
            float overflow = ComputeRotatedOverflow(head, collisionOffsets, flip, rotation, rx, ry);
            float overlapDeficit = skipOverlapCheck
              ? 0f
              : ComputeRotatedOverlapDeficit(head, collisionOffsets, flip, rotation, previousShape!, minSeparation);

            if (overflow <= 0f && overlapDeficit <= 0f)
            {
              fitting.Add((head, flip, rotation));
              continue;
            }

            float penalty = overflow + overlapDeficit;
            if (penalty < bestPenalty)
            {
              bestPenalty = penalty;
              bestHead = head;
              bestFlip = flip;
              bestRotation = rotation;
            }
          }
        }
      }

      if (fitting.Count > 0)
        return fitting[random.Next(fitting.Count)];

      return (ClampToPlayfield(bestHead, radius), bestFlip, bestRotation);
    }

    private static string BuildStandardLine(
      ParsedHitObject obj,
      Vector2 newPosition,
      List<Vector2>? newCurvePoints
    )
    {
      var parts = (string[])obj.Parts.Clone();
      parts[0] = ((int)MathF.Round(newPosition.X)).ToString(CultureInfo.InvariantCulture);
      parts[1] = ((int)MathF.Round(newPosition.Y)).ToString(CultureInfo.InvariantCulture);

      if (obj.IsSlider && newCurvePoints != null && parts.Length > 5)
      {
        var pointTokens = newCurvePoints.Select(p =>
          $"{(int)MathF.Round(p.X)}:{(int)MathF.Round(p.Y)}"
        );
        parts[5] = obj.CurveTypeLetter + "|" + string.Join('|', pointTokens);
      }

      return string.Join(',', parts);
    }

    private static List<string> ApplyStandard(
      IReadOnlyList<string> lines,
      decimal circleSize,
      Random random
    )
    {
      var parsed = new (string raw, ParsedHitObject? obj)[lines.Count];
      for (int i = 0; i < lines.Count; i++)
        parsed[i] = (lines[i], ParseStandardObject(lines[i]));

      float radius = ComputeCircleRadius(circleSize);

      var distances = new float[parsed.Length];
      var originalAnchor = PlayfieldCentre;
      for (int i = 0; i < parsed.Length; i++)
      {
        var obj = parsed[i].obj;
        if (obj == null)
          continue;
        if (!obj.IsSpinner)
          distances[i] = Vector2.Distance(obj.Position, originalAnchor);
        originalAnchor = obj.IsSpinner ? obj.Position : obj.Position + TailOffset(obj);
      }

      var result = new List<string>(parsed.Length);
      var newAnchor = PlayfieldCentre;
      List<Vector2>? previousShape = null;

      for (int i = 0; i < parsed.Length; i++)
      {
        var (raw, obj) = parsed[i];
        if (obj == null)
        {
          result.Add(raw);
          continue;
        }

        if (obj.IsSpinner)
        {
          result.Add(raw);
          newAnchor = obj.Position;
          previousShape = null;
          continue;
        }

        if (!obj.IsSlider)
        {
          var tapPosition = ChooseTapPosition(newAnchor, distances[i], radius, previousShape, random);
          result.Add(BuildStandardLine(obj, tapPosition, null));
          newAnchor = tapPosition;
          previousShape = new List<Vector2> { tapPosition };
          continue;
        }

        var collisionOffsets = ComputeCollisionOffsets(obj);
        var (headPosition, chosenFlip, chosenRotation) = ChooseSliderPlacement(
          newAnchor,
          distances[i],
          radius,
          collisionOffsets,
          previousShape,
          random
        );

        var rawControlOffsets = obj.CurvePoints.Select(p => p - obj.Position).ToList();
        var newCurvePoints = TransformOffsets(headPosition, rawControlOffsets, chosenFlip, chosenRotation);

        result.Add(BuildStandardLine(obj, headPosition, newCurvePoints));

        bool endsAwayFromHead = obj.Slides % 2 == 1 && newCurvePoints.Count > 0;
        newAnchor = endsAwayFromHead ? newCurvePoints[^1] : headPosition;

        previousShape = TransformOffsets(headPosition, collisionOffsets, chosenFlip, chosenRotation);
      }

      return result;
    }

    private static List<string> ApplyTaiko(IReadOnlyList<string> lines, Random random)
    {
      const int normalBit = 1;
      const int whistleBit = 2;
      const int finishBit = 4;

      var result = new List<string>(lines.Count);

      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("//"))
        {
          result.Add(raw);
          continue;
        }

        var parts = line.Split(',');
        if (
          parts.Length < 5
          || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
          || !int.TryParse(
            parts[4],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int hitSound
          )
        )
        {
          result.Add(raw);
          continue;
        }

        bool isNormalNote = (type & 1) != 0 && (type & 2) == 0 && (type & 8) == 0;
        if (!isNormalNote)
        {
          result.Add(raw);
          continue;
        }

        bool isKat = random.Next(2) == 0;
        int newHitSound = normalBit | (hitSound & finishBit) | (isKat ? whistleBit : 0);

        parts[4] = newHitSound.ToString(CultureInfo.InvariantCulture);
        result.Add(string.Join(',', parts));
      }

      return result;
    }

    private static List<string> ApplyMania(
      IReadOnlyList<string> lines,
      decimal circleSize,
      Random random
    )
    {
      int columnCount = Math.Max(1, (int)Math.Round(circleSize));
      var permutation = Enumerable.Range(0, columnCount).ToArray();
      Shuffle(permutation, random);

      var result = new List<string>(lines.Count);

      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("//"))
        {
          result.Add(raw);
          continue;
        }

        var parts = line.Split(',');
        if (
          parts.Length < 4
          || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
        )
        {
          result.Add(raw);
          continue;
        }

        int column = Math.Clamp((int)(x * columnCount / PlayfieldWidth), 0, columnCount - 1);
        int newColumn = permutation[column];
        float newX = (newColumn + 0.5f) * PlayfieldWidth / columnCount;

        parts[0] = ((int)MathF.Round(newX)).ToString(CultureInfo.InvariantCulture);
        result.Add(string.Join(',', parts));
      }

      return result;
    }

    private static void Shuffle<T>(T[] array, Random random)
    {
      for (int i = array.Length - 1; i > 0; i--)
      {
        int j = random.Next(i + 1);
        (array[i], array[j]) = (array[j], array[i]);
      }
    }
  }
}
