using System.Windows;

namespace OsuMate.Utils
{
    /// <summary>
    /// DataGridColumn など視覚ツリーに属さない（＝DataContextを継承しない）要素から
    /// 通常のBindingでViewModelのプロパティを参照するためのプロキシ。
    /// Freezableを継承することでツリー外でもDataContextの変更通知を受け取れる。
    /// 使い方: UserControl.Resources に
    ///   &lt;utils:BindingProxy x:Key="Proxy" Data="{Binding}"/&gt;
    /// を置き、DataGridColumn側から
    ///   Visibility="{Binding Data.SomeProperty, Source={StaticResource Proxy}}"
    /// のように参照する。
    /// </summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }
}
