using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
  public class URBarRenderer
  {
    private const int MaxHitPolys = 30;
    private const int MaxSegmentRects = 6;
    private const int MaxValueLabels = 20;

    private static readonly PointCollection DiamondPoints = CreateDiamondPoints();

    public TimeSpan AvgLineAnimDuration { get; set; } = TimeSpan.FromMilliseconds(800);

    private readonly Canvas _canvas;
    private readonly CanvasElementPool<Rectangle> _rectPool;
    private readonly CanvasElementPool<Polygon> _polyPool;
    private readonly CanvasElementPool<TextBlock> _labelPool;

    private readonly Rectangle _centerLine = new() { Fill = Brushes.White };

    private readonly Polygon _redMarkerNear = new() { Fill = Brushes.Red };
    private readonly Polygon _redMarkerFar = new() { Fill = Brushes.Red };
    private bool _redLineVisible;

    public double AvgLineFollowStrength { get; set; } = 0.1;
    private double _floatingAverage;
    private int _processedHitCount;

    public double SegmentOpacity { get; set; } = 0.2;

    public double MarkerOpacity { get; set; } = 0.5;

    public double LabelOpacity { get; set; } = 0.75;

    public double HitErrorOpacity { get; set; } = 1.0;

    private double _valueLabelFontSize = 1;

    private enum LabelAnchor
    {
      TopCenter,
      BottomCenter,
      LeftCenter,
      RightCenter,
    }

    public URBarRenderer(Canvas canvas)
    {
      _canvas = canvas;
      _rectPool = new CanvasElementPool<Rectangle>(canvas, () => new Rectangle(), MaxSegmentRects);
      _polyPool = new CanvasElementPool<Polygon>(canvas, CreatePoolPolygon, MaxHitPolys);

      _canvas.Children.Add(_centerLine);

      _labelPool = new CanvasElementPool<TextBlock>(canvas, CreatePoolLabel, MaxValueLabels);

      _redMarkerNear.Visibility = Visibility.Collapsed;
      _redMarkerFar.Visibility = Visibility.Collapsed;
      _canvas.Children.Add(_redMarkerNear);
      _canvas.Children.Add(_redMarkerFar);
    }

    private static PointCollection CreateDiamondPoints()
    {
      var points = new PointCollection
      {
        new Point(0.5, 0),
        new Point(1, 0.5),
        new Point(0.5, 1),
        new Point(0, 0.5),
      };
      points.Freeze();
      return points;
    }

    private static Polygon CreatePoolPolygon() =>
      new() { Points = DiamondPoints, Stretch = Stretch.Fill };

    private static TextBlock CreatePoolLabel() => new() { Foreground = Brushes.White };

    public void Render(URBarViewModel vm, int rotation, double valueLabelFontSize)
    {
      _valueLabelFontSize = valueLabelFontSize;

      double markerOpacity = Math.Clamp(MarkerOpacity, 0, 1);
      _centerLine.Opacity = markerOpacity;
      _redMarkerNear.Opacity = markerOpacity;
      _redMarkerFar.Opacity = markerOpacity;

      _rectPool.Reset();
      _polyPool.Reset();
      _labelPool.Reset();

      double meh = vm.GetMaxWindow();
      bool sideways = rotation == 90 || rotation == 270;
      bool flipped = rotation == 180 || rotation == 270;

      if (sideways)
        RenderVertical(vm, meh, flipped);
      else
        RenderHorizontal(vm, meh, flipped);
    }

    private void RenderHorizontal(URBarViewModel vm, double meh, bool flipped)
    {
      var segments = vm.GetCenterLineSegments();
      double canvasW = _canvas.ActualWidth;
      double canvasH = _canvas.ActualHeight;

      double barThick = canvasH * 0.10;

      double hitThick = canvasH * 0.70;
      double hitWidth = canvasW * 0.03;
      double whiteLineWidth = canvasW * 0.01;

      double centerY = canvasH / 2 - barThick / 2;

      _centerLine.Width = whiteLineWidth;
      _centerLine.Height = canvasH;
      Canvas.SetLeft(_centerLine, canvasW / 2 - whiteLineWidth / 2);
      Canvas.SetTop(_centerLine, 0);

      double labelGap = Math.Max(1, canvasH * 0.015);

      foreach (var (judgement, msValue, from, to) in segments.OrderBy(s => s.from))
      {
        double f = flipped ? 1 - to : from;
        double t = flipped ? 1 - from : to;
        var rect = _rectPool.Get();
        rect.Width = Math.Max(0, (t - f) * canvasW);
        rect.Height = barThick;
        rect.Fill = ColorUtils.ColorBrushAlpha(
          ColorUtils.JudgementColor(judgement),
          (byte)(Math.Clamp(SegmentOpacity, 0, 1) * 255)
        );
        Canvas.SetLeft(rect, f * canvasW);
        Canvas.SetTop(rect, centerY);

        string label = FormatMs(msValue);
        double xEarly = f * canvasW;
        double xLate = t * canvasW;
        bool earlyAtEdge = xEarly <= 0.5;
        bool lateAtEdge = xLate >= canvasW - 0.5;

        if (!earlyAtEdge)
        {
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            xEarly,
            centerY - labelGap,
            LabelAnchor.BottomCenter
          );
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            xEarly,
            centerY + barThick + labelGap,
            LabelAnchor.TopCenter
          );
        }
        if (!lateAtEdge)
        {
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            xLate,
            centerY - labelGap,
            LabelAnchor.BottomCenter
          );
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            xLate,
            centerY + barThick + labelGap,
            LabelAnchor.TopCenter
          );
        }
      }

      if (meh == 0)
      {
        HideRedLine();
        return;
      }

      var hitErrors = vm.HitErrors;
      int recentCount = Math.Min(MaxHitPolys, hitErrors.Count);

      for (int i = 0; i < recentCount; i++)
      {
        int value = hitErrors[hitErrors.Count - 1 - i];
        double v = flipped ? -value : value;
        double x = canvasW * ((v + meh) / (meh * 2));
        int alpha = (int)(Math.Max(0, 170 - i * 2) * Math.Clamp(HitErrorOpacity, 0, 1));
        var poly = _polyPool.Get();
        poly.Width = hitWidth;
        poly.Height = hitThick;
        poly.Fill = ColorUtils.ColorBrushAlpha(
          ColorUtils.JudgementColor(vm.GetJudgement(value)),
          (byte)alpha
        );
        Canvas.SetLeft(poly, x - hitWidth / 2);
        Canvas.SetTop(poly, (canvasH - hitThick) / 2);
      }

      if (recentCount > 0)
      {
        double avg = UpdateFloatingAverage(vm);
        double v = flipped ? -avg : avg;
        double x = canvasW * Math.Clamp((v + meh) / (meh * 2), 0, 1);
        PositionRedLineHorizontal(x, canvasH);
      }
      else
      {
        HideRedLine();
      }
    }

    private void RenderVertical(URBarViewModel vm, double meh, bool flipped)
    {
      var segments = vm.GetCenterLineSegments();
      double canvasW = _canvas.ActualWidth;
      double canvasH = _canvas.ActualHeight;

      double barThick = canvasW * 0.10;

      double hitThick = canvasW * 0.70;
      double hitHeight = canvasH * 0.03;
      double whiteLineHeight = canvasH * 0.01;

      double centerX = canvasW / 2 - barThick / 2;

      _centerLine.Width = canvasW;
      _centerLine.Height = whiteLineHeight;
      Canvas.SetLeft(_centerLine, 0);
      Canvas.SetTop(_centerLine, canvasH / 2 - whiteLineHeight / 2);

      double labelGapV = Math.Max(1, canvasW * 0.015);

      foreach (var (judgement, msValue, from, to) in segments.OrderBy(s => s.from))
      {
        double f = flipped ? 1 - to : from;
        double t = flipped ? 1 - from : to;
        var rect = _rectPool.Get();
        rect.Width = barThick;
        rect.Height = Math.Max(0, (t - f) * canvasH);
        rect.Fill = ColorUtils.ColorBrushAlpha(
          ColorUtils.JudgementColor(judgement),
          (byte)(Math.Clamp(SegmentOpacity, 0, 1) * 255)
        );
        Canvas.SetLeft(rect, centerX);
        Canvas.SetTop(rect, f * canvasH);

        string label = FormatMs(msValue);
        double yEarly = f * canvasH;
        double yLate = t * canvasH;
        bool earlyAtEdge = yEarly <= 0.5;
        bool lateAtEdge = yLate >= canvasH - 0.5;

        if (!earlyAtEdge)
        {
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            centerX - labelGapV,
            yEarly,
            LabelAnchor.RightCenter
          );
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            centerX + barThick + labelGapV,
            yEarly,
            LabelAnchor.LeftCenter
          );
        }
        if (!lateAtEdge)
        {
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            centerX - labelGapV,
            yLate,
            LabelAnchor.RightCenter
          );
          PlaceValueLabel(
            _labelPool.Get(),
            label,
            centerX + barThick + labelGapV,
            yLate,
            LabelAnchor.LeftCenter
          );
        }
      }

      if (meh == 0)
      {
        HideRedLine();
        return;
      }

      var hitErrors = vm.HitErrors;
      int recentCount = Math.Min(MaxHitPolys, hitErrors.Count);

      for (int i = 0; i < recentCount; i++)
      {
        int value = hitErrors[hitErrors.Count - 1 - i];
        double v = flipped ? -value : value;
        double y = canvasH * ((v + meh) / (meh * 2));
        int alpha = (int)(Math.Max(0, 170 - i * 2) * Math.Clamp(HitErrorOpacity, 0, 1));
        var poly = _polyPool.Get();
        poly.Width = hitThick;
        poly.Height = hitHeight;
        poly.Fill = ColorUtils.ColorBrushAlpha(
          ColorUtils.JudgementColor(vm.GetJudgement(value)),
          (byte)alpha
        );
        Canvas.SetLeft(poly, (canvasW - hitThick) / 2);
        Canvas.SetTop(poly, y - hitHeight / 2);
      }

      if (recentCount > 0)
      {
        double avg = UpdateFloatingAverage(vm);
        double v = flipped ? -avg : avg;
        double y = canvasH * Math.Clamp((v + meh) / (meh * 2), 0, 1);
        PositionRedLineVertical(y, canvasW);
      }
      else
      {
        HideRedLine();
      }
    }

    private double UpdateFloatingAverage(URBarViewModel vm)
    {
      var hitErrors = vm.HitErrors;
      int totalCount = vm.HitErrorTotalCount;

      if (totalCount < _processedHitCount)
      {
        _processedHitCount = 0;
        _floatingAverage = 0;
      }

      int newCount = Math.Min(totalCount - _processedHitCount, hitErrors.Count);
      for (int i = hitErrors.Count - newCount; i < hitErrors.Count; i++)
        _floatingAverage =
          _floatingAverage * (1 - AvgLineFollowStrength) + hitErrors[i] * AvgLineFollowStrength;

      _processedHitCount = totalCount;
      return _floatingAverage;
    }

    private const double AvgMarkerHeightRatio = 0.3;

    private const double AvgMarkerGapRatio = 0.70;

    private void PositionRedLineHorizontal(double x, double canvasH)
    {
      double crossSize = canvasH;
      double triHeight = crossSize * AvgMarkerHeightRatio;
      double triBase = triHeight * 2 / Math.Sqrt(3);
      double gap = crossSize * AvgMarkerGapRatio;

      double centerY = canvasH / 2;
      double nearTopY = centerY - gap / 2 - triHeight;
      double farTopY = centerY + gap / 2;

      _redMarkerNear.Points = new PointCollection
      {
        new Point(0, 0),
        new Point(triBase, 0),
        new Point(triBase / 2, triHeight),
      };
      _redMarkerFar.Points = new PointCollection
      {
        new Point(0, triHeight),
        new Point(triBase, triHeight),
        new Point(triBase / 2, 0),
      };

      _redMarkerNear.BeginAnimation(Canvas.TopProperty, null);
      _redMarkerFar.BeginAnimation(Canvas.TopProperty, null);
      Canvas.SetTop(_redMarkerNear, nearTopY);
      Canvas.SetTop(_redMarkerFar, farTopY);

      AnimateRedLineTo(Canvas.LeftProperty, x - triBase / 2);
    }

    private void PositionRedLineVertical(double y, double canvasW)
    {
      double crossSize = canvasW;
      double triWidth = crossSize * AvgMarkerHeightRatio;
      double triBase = triWidth * 2 / Math.Sqrt(3);
      double gap = crossSize * AvgMarkerGapRatio;

      double centerX = canvasW / 2;
      double nearLeftX = centerX - gap / 2 - triWidth;
      double farLeftX = centerX + gap / 2;

      _redMarkerNear.Points = new PointCollection
      {
        new Point(0, 0),
        new Point(0, triBase),
        new Point(triWidth, triBase / 2),
      };
      _redMarkerFar.Points = new PointCollection
      {
        new Point(triWidth, 0),
        new Point(triWidth, triBase),
        new Point(0, triBase / 2),
      };

      _redMarkerNear.BeginAnimation(Canvas.LeftProperty, null);
      _redMarkerFar.BeginAnimation(Canvas.LeftProperty, null);
      Canvas.SetLeft(_redMarkerNear, nearLeftX);
      Canvas.SetLeft(_redMarkerFar, farLeftX);

      AnimateRedLineTo(Canvas.TopProperty, y - triBase / 2);
    }

    private void AnimateRedLineTo(DependencyProperty axisProperty, double target)
    {
      bool justAppeared = !_redLineVisible;
      _redLineVisible = true;
      _redMarkerNear.Visibility = Visibility.Visible;
      _redMarkerFar.Visibility = Visibility.Visible;

      if (justAppeared)
      {
        _redMarkerNear.BeginAnimation(axisProperty, null);
        _redMarkerFar.BeginAnimation(axisProperty, null);
        _redMarkerNear.SetValue(axisProperty, target);
        _redMarkerFar.SetValue(axisProperty, target);
        return;
      }

      foreach (var marker in new[] { _redMarkerNear, _redMarkerFar })
      {
        var anim = new DoubleAnimation
        {
          To = target,
          Duration = AvgLineAnimDuration,
          EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        };
        marker.BeginAnimation(axisProperty, anim);
      }
    }

    private void HideRedLine()
    {
      if (!_redLineVisible)
        return;
      _redLineVisible = false;
      foreach (var marker in new[] { _redMarkerNear, _redMarkerFar })
      {
        marker.BeginAnimation(Canvas.LeftProperty, null);
        marker.BeginAnimation(Canvas.TopProperty, null);
        marker.Visibility = Visibility.Collapsed;
      }
    }

    private static string FormatMs(double ms) => ms.ToString("F2", CultureInfo.InvariantCulture);

    private void PlaceValueLabel(TextBlock tb, string text, double x, double y, LabelAnchor anchor)
    {
      tb.Text = text;
      tb.FontSize = _valueLabelFontSize;
      tb.Opacity = Math.Clamp(LabelOpacity, 0, 1);

      var dpi = VisualTreeHelper.GetDpi(_canvas).PixelsPerDip;
      var formatted = new FormattedText(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
        _valueLabelFontSize,
        Brushes.White,
        dpi
      );

      double left,
        top;
      switch (anchor)
      {
        case LabelAnchor.TopCenter:
          left = x - formatted.Width / 2;
          top = y;
          break;
        case LabelAnchor.BottomCenter:
          left = x - formatted.Width / 2;
          top = y - formatted.Height;
          break;
        case LabelAnchor.LeftCenter:
          left = x;
          top = y - formatted.Height / 2;
          break;
        default:
          left = x - formatted.Width;
          top = y - formatted.Height / 2;
          break;
      }
      Canvas.SetLeft(tb, left);
      Canvas.SetTop(tb, top);
    }
  }
}
