using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OsuMate.Views.Behaviors
{
    /// <summary>リサイズハンドルの配置方向。</summary>
    public enum NativeResizeDirection
    {
        Left = 1,
        Right = 2,
        Top = 3,
        TopLeft = 4,
        TopRight = 5,
        Bottom = 6,
        BottomLeft = 7,
        BottomRight = 8,
    }

    /// <summary>
    /// <see cref="NativeResizeBehavior"/> を使う Window 側が、リサイズ開始・終了のタイミングで
    /// フックしたい処理（プレビュー用の変形リセット、確定描画の再実行等）を実装するためのインターフェース。
    /// </summary>
    public interface INativeResizeHost
    {
        /// <summary>OSネイティブのリサイズループに入る直前に呼ばれる。</summary>
        void OnNativeResizeStarted();

        /// <summary>OSネイティブのリサイズループを抜けた直後に呼ばれる。</summary>
        void OnNativeResizeCompleted();
    }

    /// <summary>
    /// 指定した方向（<see cref="NativeResizeDirectionProperty"/>）でOSネイティブのウィンドウリサイズ
    /// （WM_SYSCOMMAND + SC_SIZE）を開始する添付ビヘイビア。
    /// リサイズハンドル用の要素に behaviors:NativeResizeBehavior.Direction="Right" を指定するだけで、
    /// MouseLeftButtonDownの購読・Win32 API呼び出しをコードビハインドから排除できる。
    ///
    /// 自前で MouseMove を追ってサイズ計算すると、Width/Height と Left/Top の反映が1フレームずれる
    /// タイミングがあり、左端・上端のリサイズでウィンドウが震えて見えることがある。
    /// OSネイティブのリサイズ処理（DefWindowProc内のサイズ変更ループ）に丸投げすると、
    /// OS自身がサイズと位置をアトミックに同期して更新するため震えなくなる。
    /// </summary>
    public static class NativeResizeBehavior
    {
        private const int WM_SYSCOMMAND = 0x112;
        private const int SC_SIZE = 0xF000;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.RegisterAttached(
                "Direction",
                typeof(NativeResizeDirection?),
                typeof(NativeResizeBehavior),
                new PropertyMetadata(null, OnDirectionChanged));

        public static void SetDirection(UIElement element, NativeResizeDirection? value) => element.SetValue(DirectionProperty, value);
        public static NativeResizeDirection? GetDirection(UIElement element) => (NativeResizeDirection?)element.GetValue(DirectionProperty);

        private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            // 差し替え時に二重登録しないよう、一度外してから必要なら付け直す
            element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            if (e.NewValue != null)
                element.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement element) return;
            var direction = GetDirection(element);
            if (direction == null) return;

            var window = Window.GetWindow(element);
            if (window == null) return;

            e.Handled = true;
            BeginResize(window, direction.Value);
        }

        /// <summary>
        /// OSネイティブのリサイズループを開始する。呼び出し元が <see cref="INativeResizeHost"/> を
        /// 実装していれば、開始直前・終了直後のフックを呼び出す。
        /// </summary>
        public static void BeginResize(Window window, NativeResizeDirection direction)
        {
            var host = window as INativeResizeHost;
            host?.OnNativeResizeStarted();

            // このBorderが暗黙的に持っている可能性のあるマウスキャプチャを解放しないと、
            // OS側のリサイズループにマウス入力が渡らない。
            ReleaseCapture();

            // SendMessageはユーザーがボタンを離すまでブロックするが、その間もDefWindowProcが
            // 内部でメッセージポンプを回してくれるため、SizeChanged/LocationChangedは
            // 通常通りリアルタイムに発火し続ける（UIスレッドがフリーズするわけではない）。
            var handle = new WindowInteropHelper(window).Handle;
            SendMessage(handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + (int)direction), IntPtr.Zero);

            host?.OnNativeResizeCompleted();
        }
    }
}
