using System.Windows;
using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// Canvas上に配置する図形要素（Rectangle/Polygon/TextBlock等）を再利用するための汎用オブジェクトプール。
    /// 高頻度描画（毎フレームのRender）でのGCアロケーションを避けるために使う
    /// （URBarWindow の判定バー描画から切り出し）。
    /// </summary>
    public class CanvasElementPool<T> where T : FrameworkElement
    {
        private readonly Canvas _canvas;
        private readonly Func<T> _factory;
        private readonly List<T> _pool = [];
        private int _cursor;

        /// <param name="canvas">要素を追加する先のCanvas。</param>
        /// <param name="factory">プールが枯渇したときに新しい要素を生成するデリゲート。</param>
        /// <param name="prewarmCount">事前に生成しておく要素数。</param>
        public CanvasElementPool(Canvas canvas, Func<T> factory, int prewarmCount = 0)
        {
            _canvas = canvas;
            _factory = factory;
            for (int i = 0; i < prewarmCount; i++) Add();
        }

        private T Add()
        {
            var element = _factory();
            element.Visibility = Visibility.Collapsed;
            _pool.Add(element);
            _canvas.Children.Add(element);
            return element;
        }

        /// <summary>プールから1つ取得し、Visibleにして返す。プールが枯渇していれば新規生成する。</summary>
        public T Get()
        {
            var element = _cursor < _pool.Count ? _pool[_cursor] : Add();
            _cursor++;
            element.Visibility = Visibility.Visible;
            return element;
        }

        /// <summary>今回のRenderで使った分だけを非表示に戻し、次回のRenderに向けてカーソルを先頭に戻す。</summary>
        public void Reset()
        {
            for (int i = 0; i < _cursor; i++)
                _pool[i].Visibility = Visibility.Collapsed;
            _cursor = 0;
        }
    }
}
