using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// ドラッグ&amp;ドロップ並び替え中に、ドラッグ中のアイテムをマウス位置に追従させて表示するAdorner。
    /// <see cref="DragReorderController{T}"/> の実装詳細であり、単体では使わない。
    /// </summary>
    internal class DragGhostAdorner : Adorner
    {
        private readonly BitmapSource _bitmap;
        private Point _mousePos;
        private readonly double _offsetY;
        private readonly double _fixedX;

        public DragGhostAdorner(UIElement adornedElement, BitmapSource bitmap, double offsetY, double fixedX)
            : base(adornedElement)
        {
            _bitmap = bitmap;
            _offsetY = offsetY;
            _fixedX = fixedX;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point mousePos)
        {
            _mousePos = mousePos;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(
                _fixedX,
                _mousePos.Y - _offsetY,
                _bitmap.Width,
                _bitmap.Height);
            dc.PushOpacity(0.85);
            dc.DrawImage(_bitmap, rect);
            dc.Pop();
        }
    }
}
