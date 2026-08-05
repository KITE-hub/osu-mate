using OsuMate.Utils;
using OsuMate.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// URBarWindow の判定バー描画（横向き・縦向き、hit窓の色分け、直近打鍵の表示等）を担う。
    /// Window側はライフサイクル管理（ドラッグ・リサイズ・設定モード切替）に専念できるよう、
    /// CompositionTarget.Rendering起点のカスタム描画ループの実体をここへ切り出している。
    /// </summary>
    public class URBarRenderer
    {
        // ── オブジェクトプーリング ────────────────────────────────────────
        // 最大表示数分の図形を事前生成して使い回す（GC 圧を削減）
        private const int MaxHitPolys = 30;
        private const int MaxSegmentRects = 6;  // judgement 帯は最大 5 + center 1
        private const int MaxValueLabels = 20;   // judgement最大5種 × (early/late) × (上/下 or 左/右)

        private static readonly PointCollection DiamondPoints = CreateDiamondPoints();

        // 直近打鍵位置（赤線＝平均線）の移動アニメーション時間・イージング。
        // osu!(lazer)はBarHitErrorMeter.OnNewJudgementにあるarrow.MoveToY(..., 800, Easing.OutQuint)
        // SettingsViewのURBarセクション（Avg Line Anim Duration）から変更可能なため、
        // 既定値を持つインスタンスプロパティにしている。
        public TimeSpan AvgLineAnimDuration { get; set; } = TimeSpan.FromMilliseconds(800);

        private readonly Canvas _canvas;
        private readonly CanvasElementPool<Rectangle> _rectPool;
        private readonly CanvasElementPool<Polygon> _polyPool;
        private readonly CanvasElementPool<TextBlock> _labelPool;

        // 中心線（白線）専用の要素。プールから取らず常に同一インスタンスを使い回すことで、
        private readonly Rectangle _centerLine = new() { Fill = Brushes.White };

        // 赤線（直近打鍵位置の平均値マーカー）専用の要素。中心線を挟んで向き合う2つの正三角形として表現
        private readonly Polygon _redMarkerNear = new() { Fill = Brushes.Red };
        private readonly Polygon _redMarkerFar = new() { Fill = Brushes.Red };
        private bool _redLineVisible;

        // 赤線（平均線）の「目標値」を生の最新打鍵ではなく指数移動平均(EMA)にするための状態。
        // osu!(lazer)は floatingAverage = floatingAverage * 0.9 + judgement.TimeOffset * 0.1
        // これにより1発だけ大きく外れた打鍵が来ても、目標値自体は一気には動かない。
        // AvgLineFollowStrengthが「hitErrors[i]側」の重み（既定0.1）で、SettingsViewのURBarセクション
        // （Avg Line Follow Strength）から変更可能。1にすると平均を無効化して最新打鍵へ瞬時に追従する。
        public double AvgLineFollowStrength { get; set; } = 0.1;
        private double _floatingAverage;
        private int _processedHitCount;

        // barThick（判定色帯）の不透明度
        public double SegmentOpacity { get; set; } = 0.2;
        // 白線(中心線)と赤マーカー(Avg)の不透明度
        public double MarkerOpacity { get; set; } = 0.5;
        // ラベル（判定幅の数値、EARLY/LATE）の不透明度
        public double LabelOpacity { get; set; } = 0.75;
        // 判定ドット（_polyPool）の不透明度
        public double HitErrorOpacity { get; set; } = 1.0;

        private double _valueLabelFontSize = 1;

        private enum LabelAnchor { TopCenter, BottomCenter, LeftCenter, RightCenter }

        public URBarRenderer(Canvas canvas)
        {
            _canvas = canvas;
            _rectPool  = new CanvasElementPool<Rectangle>(canvas, () => new Rectangle(), MaxSegmentRects);
            _polyPool  = new CanvasElementPool<Polygon>(canvas, CreatePoolPolygon, MaxHitPolys);

            // 重なり順（奥→手前）: barThick(_rectPool) → 判定ドット(_polyPool) →
            // 中心線(_centerLine) → ラベル(_labelPool) → 赤三角マーカー(_redMarkerNear/_redMarkerFar)
            _canvas.Children.Add(_centerLine);

            _labelPool = new CanvasElementPool<TextBlock>(canvas, CreatePoolLabel, MaxValueLabels);

            _redMarkerNear.Visibility = Visibility.Collapsed;
            _redMarkerFar.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(_redMarkerNear);
            _canvas.Children.Add(_redMarkerFar);
        }

        private static PointCollection CreateDiamondPoints()
        {
            var points = new PointCollection { new Point(0.5, 0), new Point(1, 0.5), new Point(0.5, 1), new Point(0, 0.5) };
            points.Freeze();
            return points;
        }

        private static Polygon CreatePoolPolygon() => new() { Points = DiamondPoints, Stretch = Stretch.Fill };
        private static TextBlock CreatePoolLabel() => new() { Foreground = Brushes.White };

        /// <summary>
        /// 判定バーを描画する。回転（0/90/180/270）に応じて横向き・縦向きの描画に振り分ける。
        /// </summary>
        /// <param name="valueLabelFontSize">hit window値ラベル用のフォントサイズ（Window側のUpdateLabelsで計算済みの値）。</param>
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
            bool flipped  = rotation == 180 || rotation == 270;

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
                rect.Fill = ColorUtils.ColorBrushAlpha(ColorUtils.JudgementColor(judgement), (byte)(Math.Clamp(SegmentOpacity, 0, 1) * 255));
                Canvas.SetLeft(rect, f * canvasW);
                Canvas.SetTop(rect, centerY);

                // 色の境目（barThickの上下）にGetModifiedHitWindowsの値を表示
                // ただし一番外側（表示領域の端）に来る境界はラベルが途切れるため非表示にする
                string label = FormatMs(msValue);
                double xEarly = f * canvasW;
                double xLate = t * canvasW;
                bool earlyAtEdge = xEarly <= 0.5;
                bool lateAtEdge = xLate >= canvasW - 0.5;

                if (!earlyAtEdge)
                {
                    PlaceValueLabel(_labelPool.Get(), label, xEarly, centerY - labelGap, LabelAnchor.BottomCenter);
                    PlaceValueLabel(_labelPool.Get(), label, xEarly, centerY + barThick + labelGap, LabelAnchor.TopCenter);
                }
                if (!lateAtEdge)
                {
                    PlaceValueLabel(_labelPool.Get(), label, xLate, centerY - labelGap, LabelAnchor.BottomCenter);
                    PlaceValueLabel(_labelPool.Get(), label, xLate, centerY + barThick + labelGap, LabelAnchor.TopCenter);
                }
            }

            if (meh == 0)
            {
                HideRedLine();
                return;
            }

            var values = vm.HitErrors.TakeLast(30).ToList();
            values.Reverse();

            for (int i = 0; i < values.Count; i++)
            {
                double v = flipped ? -values[i] : values[i];
                double x = canvasW * ((v + meh) / (meh * 2));
                int alpha = (int)(Math.Max(0, 170 - i * 2) * Math.Clamp(HitErrorOpacity, 0, 1));
                var poly = _polyPool.Get();
                poly.Width = hitWidth;
                poly.Height = hitThick;
                poly.Fill = ColorUtils.ColorBrushAlpha(ColorUtils.JudgementColor(vm.GetJudgement(values[i])), (byte)alpha);
                Canvas.SetLeft(poly, x - hitWidth / 2);
                Canvas.SetTop(poly, (canvasH - hitThick) / 2);
            }

            if (values.Count > 0)
            {
                double avg = UpdateFloatingAverage(vm.HitErrors);
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
                rect.Fill = ColorUtils.ColorBrushAlpha(ColorUtils.JudgementColor(judgement), (byte)(Math.Clamp(SegmentOpacity, 0, 1) * 255));
                Canvas.SetLeft(rect, centerX);
                Canvas.SetTop(rect, f * canvasH);

                // 色の境目（barThickの左右）にGetModifiedHitWindowsの値を表示
                // ただし一番外側（表示領域の端）に来る境界はラベルが途切れるため非表示にする
                string label = FormatMs(msValue);
                double yEarly = f * canvasH;
                double yLate = t * canvasH;
                bool earlyAtEdge = yEarly <= 0.5;
                bool lateAtEdge = yLate >= canvasH - 0.5;

                if (!earlyAtEdge)
                {
                    PlaceValueLabel(_labelPool.Get(), label, centerX - labelGapV, yEarly, LabelAnchor.RightCenter);
                    PlaceValueLabel(_labelPool.Get(), label, centerX + barThick + labelGapV, yEarly, LabelAnchor.LeftCenter);
                }
                if (!lateAtEdge)
                {
                    PlaceValueLabel(_labelPool.Get(), label, centerX - labelGapV, yLate, LabelAnchor.RightCenter);
                    PlaceValueLabel(_labelPool.Get(), label, centerX + barThick + labelGapV, yLate, LabelAnchor.LeftCenter);
                }
            }

            if (meh == 0)
            {
                HideRedLine();
                return;
            }

            var values = vm.HitErrors.TakeLast(30).ToList();
            values.Reverse();

            for (int i = 0; i < values.Count; i++)
            {
                double v = flipped ? -values[i] : values[i];
                double y = canvasH * ((v + meh) / (meh * 2));
                int alpha = (int)(Math.Max(0, 170 - i * 2) * Math.Clamp(HitErrorOpacity, 0, 1));
                var poly = _polyPool.Get();
                poly.Width = hitThick;
                poly.Height = hitHeight;
                poly.Fill = ColorUtils.ColorBrushAlpha(ColorUtils.JudgementColor(vm.GetJudgement(values[i])), (byte)alpha);
                Canvas.SetLeft(poly, (canvasW - hitThick) / 2);
                Canvas.SetTop(poly, y - hitHeight / 2);
            }

            if (values.Count > 0)
            {
                double avg = UpdateFloatingAverage(vm.HitErrors);
                double v = flipped ? -avg : avg;
                double y = canvasH * Math.Clamp((v + meh) / (meh * 2), 0, 1);
                PositionRedLineVertical(y, canvasW);
            }
            else
            {
                HideRedLine();
            }
        }

        // osu!(lazer)のBarHitErrorMeter.OnNewJudgementと同じ指数移動平均(EMA)。
        // 既定ではfloatingAverage = floatingAverage * 0.9 + newOffset * 0.1（AvgLineFollowStrength=0.1）だが、SettingsViewから変更可能。
        private double UpdateFloatingAverage(List<int> hitErrors)
        {
            if (hitErrors.Count < _processedHitCount)
            {
                // 曲が変わる等でリストがリセットされた
                _processedHitCount = 0;
                _floatingAverage = 0;
            }

            for (int i = _processedHitCount; i < hitErrors.Count; i++)
                _floatingAverage = _floatingAverage * (1 - AvgLineFollowStrength) + hitErrors[i] * AvgLineFollowStrength;

            _processedHitCount = hitErrors.Count;
            return _floatingAverage;
        }

        // ── 赤線（直近打鍵位置の平均値マーカー）─────────────────────────────
        // 中心線を挟んで向き合う2つの正三角形（間に隙間）として描画
        private const double AvgMarkerHeightRatio = 0.3;
        // 2つの三角形の間の隙間（中心線をまたいでの合計距離）が crossSize に対して占める割合。
        private const double AvgMarkerGapRatio = 0.70;

        private void PositionRedLineHorizontal(double x, double canvasH)
        {
            double crossSize = canvasH;
            double triHeight = crossSize * AvgMarkerHeightRatio;
            double triBase = triHeight * 2 / Math.Sqrt(3); // 正三角形（底辺 = 高さ×2/√3）になる比率
            double gap = crossSize * AvgMarkerGapRatio;

            double centerY = canvasH / 2;
            double nearTopY = centerY - gap / 2 - triHeight; // 上側三角形（底辺が上、頂点が下＝中心向き）
            double farTopY = centerY + gap / 2;              // 下側三角形（頂点が上＝中心向き、底辺が下）

            _redMarkerNear.Points = new PointCollection
            {
                new Point(0, 0), new Point(triBase, 0), new Point(triBase / 2, triHeight)
            };
            _redMarkerFar.Points = new PointCollection
            {
                new Point(0, triHeight), new Point(triBase, triHeight), new Point(triBase / 2, 0)
            };

            // 縦向きから切り替わった直後に前のアニメーション/値が残らないようTopを止めてから固定
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
            double triBase = triWidth * 2 / Math.Sqrt(3); // 正三角形になる比率
            double gap = crossSize * AvgMarkerGapRatio;

            double centerX = canvasW / 2;
            double nearLeftX = centerX - gap / 2 - triWidth; // 左側三角形（底辺が左、頂点が右＝中心向き）
            double farLeftX = centerX + gap / 2;             // 右側三角形（頂点が左＝中心向き、底辺が右）

            _redMarkerNear.Points = new PointCollection
            {
                new Point(0, 0), new Point(0, triBase), new Point(triWidth, triBase / 2)
            };
            _redMarkerFar.Points = new PointCollection
            {
                new Point(triWidth, 0), new Point(triWidth, triBase), new Point(0, triBase / 2)
            };

            _redMarkerNear.BeginAnimation(Canvas.LeftProperty, null);
            _redMarkerFar.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(_redMarkerNear, nearLeftX);
            Canvas.SetLeft(_redMarkerFar, farLeftX);

            AnimateRedLineTo(Canvas.TopProperty, y - triBase / 2);
        }

        // WPFのDoubleAnimation + BeginAnimationにより現在の描画位置から新しい判定の位置へ
        // EaseOutで滑らかに移動。近・遠2つの三角形は同じtarget値で同期して動かす。
        private void AnimateRedLineTo(DependencyProperty axisProperty, double target)
        {
            bool justAppeared = !_redLineVisible;
            _redLineVisible = true;
            _redMarkerNear.Visibility = Visibility.Visible;
            _redMarkerFar.Visibility = Visibility.Visible;

            if (justAppeared)
            {
                // 非表示状態から現れる瞬間は、画面端やゼロ位置からのスライドインを避けるため
                // アニメーションさせず即座に配置する
                _redMarkerNear.BeginAnimation(axisProperty, null);
                _redMarkerFar.BeginAnimation(axisProperty, null);
                _redMarkerNear.SetValue(axisProperty, target);
                _redMarkerFar.SetValue(axisProperty, target);
                return;
            }

            // Fromを指定しない = 現在アニメーション中の実際の表示位置から続けて滑らかにつながる
            foreach (var marker in new[] { _redMarkerNear, _redMarkerFar })
            {
                var anim = new DoubleAnimation
                {
                    To = target,
                    Duration = AvgLineAnimDuration,
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                };
                marker.BeginAnimation(axisProperty, anim);
            }
        }

        private void HideRedLine()
        {
            if (!_redLineVisible) return;
            _redLineVisible = false;
            foreach (var marker in new[] { _redMarkerNear, _redMarkerFar })
            {
                marker.BeginAnimation(Canvas.LeftProperty, null);
                marker.BeginAnimation(Canvas.TopProperty, null);
                marker.Visibility = Visibility.Collapsed;
            }
        }

        // ── hit window値ラベル ──────────────────────────────────────────

        private static string FormatMs(double ms)
            => ms.ToString("F2", CultureInfo.InvariantCulture);

        // anchor で指定した基準点にテキストの端（または中央）を合わせて配置する。
        // 例: BottomCenter → (x, y) がテキストの下端中央に来るように配置（barThickの「上」に置く時に使う）
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
                dpi);

            double left, top;
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
                default: // RightCenter
                    left = x - formatted.Width;
                    top = y - formatted.Height / 2;
                    break;
            }
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, top);
        }
    }
}
