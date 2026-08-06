using OsuMate.Utils;
using OsuMate.ViewModels;
using OsuMate.Views.Behaviors;
using OsuMate.Views.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OsuMate.Views
{
    public partial class URBarWindow : Window, INativeResizeHost
    {
        private URBarViewModel _vm = null!;
        private bool _isDraggable = false;
        public bool IsDragging { get; private set; } = false;
        public bool IsResizing { get; private set; } = false;
        public event Action<double, double>? PositionChanged;
        public event Action<double, double>? OnSizeChanged;

        // カスタムドラッグ
        private Point _dragStartMouse;
        private double _dragStartLeft;
        private double _dragStartTop;

        private int _rotation = 0;
        private double _baseWidth;
        private double _baseHeight;

        // ── リサイズ中のプレビュー表示（本描画はドラッグ終了時に1回のみ） ──────────
        // マウス移動毎のWM_SIZEで再配置（Render）を行うとコマ落ちの原因になる。
        // そのためドラッグ中は再配置を行わず、ScaleTransformで見た目のみ追従させる。
        // 座標計算等を伴わず、スケール変形によりGPU側で完結する。
        private readonly ScaleTransform _resizePreviewScale = new(1, 1);
        private double _resizeStartVisualWidth;
        private double _resizeStartVisualHeight;

        private URBarRenderer _renderer = null!;

        // EARLY/LATEラベルより一回り小さい、hit window値ラベル用のフォントサイズ
        // （UpdateLabels() で算出し、_renderer.Render() に渡す）
        private double _valueLabelFontSize = 1;

        public URBarWindow()
        {
            InitializeComponent();

            _renderer = new URBarRenderer(BarsCanvas);

            // BarsCanvas に恒久的にアタッチしておき、通常時は (1,1) で無視できるコストにしておく。
            // ドラッグ中だけ ScaleX/ScaleY を動かして安価な追従プレビューに使う。
            BarsCanvas.RenderTransformOrigin = new Point(0, 0);
            BarsCanvas.RenderTransform = _resizePreviewScale;

            // ネイティブリサイズ中（IsResizing == true の間）は OS が Width/Height/Left/Top を
            // 直接書き換える。位置変化は LocationChanged でリアルタイムに通知するが、
            // サイズ変化は中身の再配置（UpdateLabels/Render）を伴うため毎フレーム行わず、
            // ScaleTransform によるプレビューに留め、正確な再配置はドラッグ終了時に1回だけ行う
            // （OnNativeResizeStarted/OnNativeResizeCompleted 参照）。
            // （IsResizing == false のときは SetSettingsMode/SetRotation/SetCenterPosition などが
            //   自前で Width/Height/Left/Top を変更するため、ここでは何もしない）
            SizeChanged += URBarWindow_SizeChanged;
            LocationChanged += URBarWindow_LocationChanged;
        }

        private void URBarWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsResizing) return;
            bool sideways = _rotation == 90 || _rotation == 270;
            _baseWidth = sideways ? e.NewSize.Height : e.NewSize.Width;
            _baseHeight = sideways ? e.NewSize.Width : e.NewSize.Height;
            OnSizeChanged?.Invoke(_baseWidth, _baseHeight);

            // 中身の再配置はせず、ドラッグ開始時サイズとの比率でスケール変形するだけ。
            // 座標の再計算・ブラシ生成・レイアウトパスが一切発生しないため、
            // OSのリサイズ通知がどれだけ高頻度でも重くならない。
            if (_resizeStartVisualWidth > 0)
                _resizePreviewScale.ScaleX = e.NewSize.Width / _resizeStartVisualWidth;
            if (_resizeStartVisualHeight > 0)
                _resizePreviewScale.ScaleY = e.NewSize.Height / _resizeStartVisualHeight;
        }

        private void URBarWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (!IsResizing) return;
            PositionChanged?.Invoke(Left, Top);
        }

        public void SetViewModel(URBarViewModel vm)
        {
            _vm = vm;
            // CompositionTarget.Rendering でモニタのリフレッシュレートに同期して描画
            CompositionTarget.Rendering += OnRendering;
        }

        /// <summary>
        /// SettingsViewのURBarセクションで変更される「平均線の追従の強さ(AvgLineFollowStrength)」
        /// 「平均線アニメーション時間(AvgLineAnimDuration)」を _renderer へ反映する。
        /// WindowManagerServiceが起動時および該当設定の変更時に呼び出す。
        /// </summary>
        public void UpdateAnimationSettings(double avgLineFollowStrength, double avgLineAnimMs)
        {
            _renderer.AvgLineFollowStrength = avgLineFollowStrength;
            _renderer.AvgLineAnimDuration = TimeSpan.FromMilliseconds(avgLineAnimMs);
        }

        /// <summary>
        /// SettingsViewのURBarセクション（Segment/Marker/Label/Hit Error Opacity）で変更される、
        /// URBar内各要素の不透明度を反映する。EARLY/LATEラベルは_rendererの外（本Window側）に
        /// あるため、labelOpacityはここで直接そのOpacityへ適用する。
        /// WindowManagerServiceが起動時および該当設定の変更時に呼び出す。
        /// </summary>
        public void UpdateOpacitySettings(double labelOpacity, double segmentOpacity, double markerOpacity, double hitErrorOpacity)
        {
            _renderer.LabelOpacity = labelOpacity;
            _renderer.SegmentOpacity = segmentOpacity;
            _renderer.MarkerOpacity = markerOpacity;
            _renderer.HitErrorOpacity = hitErrorOpacity;

            double clampedLabelOpacity = Math.Clamp(labelOpacity, 0, 1);
            LabelEarlyH.Opacity = clampedLabelOpacity;
            LabelLateH.Opacity = clampedLabelOpacity;
            LabelEarlyV.Opacity = clampedLabelOpacity;
            LabelLateV.Opacity = clampedLabelOpacity;

            Render();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsLoaded || !IsVisible) return;
            if (_vm != null && _vm.ConsumeIsDirty())
            {
                Render();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Rendering イベントを解除しないと他の Window が開いている間もコールバックが来る
            CompositionTarget.Rendering -= OnRendering;
            base.OnClosed(e);
        }

        public void SetSettingsMode(bool enabled, double width, double height)
        {
            _isDraggable = enabled;
            this.SetClickThrough(!enabled);
            OuterBorder.BorderThickness = enabled ? new Thickness(1) : new Thickness(0);
            BackgroundBorder.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeRight.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeBottom.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeTop.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeCorner.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeCornerTopLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeCornerTopRight.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeCornerBottomLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            _baseWidth = width;
            _baseHeight = height;
            ApplyWindowSize();

            // ドラッグでのリサイズ時と同様に、サイズ変更を即座に描画へ反映する
            UpdateLayout();
            UpdateLabels();
            Render();
        }

        public void SetRotation(int degrees)
        {
            _rotation = ((degrees % 360) + 360) % 360;
            BarsCanvas.LayoutTransform = Transform.Identity;
            ApplyWindowSize();
            // SetSettingsMode と同じ理由で、Width/Height 変更をレイアウトに反映させてから描画する。
            UpdateLayout();
            UpdateLabels();
            Render();
        }

        private void ApplyWindowSize()
        {
            bool sideways = _rotation == 90 || _rotation == 270;
            Width = sideways ? _baseHeight : _baseWidth;
            Height = sideways ? _baseWidth : _baseHeight;
        }

        public void SetCenterPosition(double cx, double cy)
        {
            Left = cx - Width / 2;
            Top = cy - Height / 2;
        }

        public (double cx, double cy) GetCenterPosition()
            => (Left + Width / 2, Top + Height / 2);

        // ── ラベル ────────────────────────────────────────────────────

        private void UpdateLabels()
        {
            bool sideways = _rotation == 90 || _rotation == 270;
            bool flipped = _rotation == 180 || _rotation == 270;

            // 横向き(0°/180°)ではH、縦向き(90°/270°)ではVのラベルのみ表示する
            LabelEarlyH.Visibility = !sideways ? Visibility.Visible : Visibility.Collapsed;
            LabelLateH.Visibility = !sideways ? Visibility.Visible : Visibility.Collapsed;
            LabelEarlyV.Visibility = sideways ? Visibility.Visible : Visibility.Collapsed;
            LabelLateV.Visibility = sideways ? Visibility.Visible : Visibility.Collapsed;

            LabelEarlyH.Text = flipped ? "LATE" : "EARLY";
            LabelLateH.Text = flipped ? "EARLY" : "LATE";
            LabelEarlyV.Text = flipped ? "LATE" : "EARLY";
            LabelLateV.Text = flipped ? "EARLY" : "LATE";

            double longSide = Math.Max(Width, Height);
            double shortSide = Math.Min(Width, Height);

            double fontSize = Math.Max(1, Math.Min(longSide * 0.04, shortSide * 0.3));
            LabelEarlyH.FontSize = LabelLateH.FontSize =
            LabelEarlyV.FontSize = LabelLateV.FontSize = fontSize;

            _valueLabelFontSize = Math.Max(1, Math.Min(longSide * 0.033, shortSide * 0.25));
        }

        // ── カスタムドラッグ ──────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggable) return;
            if (e.OriginalSource is Border b &&
                (b == ResizeRight || b == ResizeLeft || b == ResizeBottom || b == ResizeTop || b == ResizeCorner ||
                 b == ResizeCornerTopLeft || b == ResizeCornerTopRight || b == ResizeCornerBottomLeft)) return;

            _dragStartMouse = PointToScreen(e.GetPosition(this));
            _dragStartLeft = Left;
            _dragStartTop = Top;
            IsDragging = true;
            CaptureMouse();
            e.Handled = true;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IsDragging) return;
            var current = PointToScreen(e.GetPosition(this));
            Left = _dragStartLeft + (current.X - _dragStartMouse.X);
            Top = _dragStartTop + (current.Y - _dragStartMouse.Y);
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsDragging) return;
            IsDragging = false;
            ReleaseMouseCapture();
            PositionChanged?.Invoke(Left, Top);
        }

        // ── リサイズ（OSネイティブ: WM_SYSCOMMAND + SC_SIZE） ────────────────
        // Win32操作は NativeResizeBehavior に分離済み。
        // 当Windowは INativeResizeHost を実装し、開始・終了時のフックのみを受け持つ。
        // リサイズ中の描画は ScaleTransform に任せて実座標の再計算は行わず、
        // 終了時の OnNativeResizeCompleted で1度だけ再配置を行う。

        public void OnNativeResizeStarted()
        {
            IsResizing = true;

            // ドラッグ開始時点のサイズを基準に、以降はこのサイズとの比率だけを
            // ScaleTransform に反映する（URBarWindow_SizeChanged 側）。
            _resizeStartVisualWidth = Width;
            _resizeStartVisualHeight = Height;
            _resizePreviewScale.ScaleX = 1;
            _resizePreviewScale.ScaleY = 1;
        }

        public void OnNativeResizeCompleted()
        {
            IsResizing = false;

            // ドラッグ終了。プレビュー用の変形を外し（等倍に戻し）、
            // 最終サイズに基づく正確な内容を1回だけ本描画
            _resizePreviewScale.ScaleX = 1;
            _resizePreviewScale.ScaleY = 1;
            UpdateLayout();
            UpdateLabels();
            Render();

            // リサイズ終了時点の最終位置を通知する
            PositionChanged?.Invoke(Left, Top);
        }

        // ── 描画 ──────────────────────────────────────────────────────
        private void Render()
        {
            if (!IsLoaded) return;

            UpdateLabels();
            _renderer.Render(_vm, _rotation, _valueLabelFontSize);
        }
    }
}
