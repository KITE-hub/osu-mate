using OxyPlot;
using System.Windows.Media;

namespace OsuMate.Utils;

internal static class ColorUtils
{
    internal static Color FromHsl(double h, double s, double l)
    {
        h /= 360;

        double r = l;
        double g = l;
        double b = l;

        if (s != 0)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return Color.FromRgb(
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255));
    }

    internal static OxyColor OxyFromHsl(double h, double s, double l)
    {
        var c = FromHsl(h, s, l);
        return OxyColor.FromRgb(c.R, c.G, c.B);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    // ── ブラシキャッシュ（旧: URBarWindow.xaml.cs / URBarRenderer.cs から集約） ──────────
    // Render() は高頻度（リサイズ中は毎フレーム）に呼ばれる箇所があるため、SolidColorBrush の
    // 使い回しでアロケーション/GC負荷を減らす。色の組み合わせは有限（judgement数 × alpha段階等）
    // なので、キャッシュはサイズが際限なく増えることはない。
    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = [];

    internal static SolidColorBrush ColorBrush(Color c)
    {
        if (_brushCache.TryGetValue(c, out var cached)) return cached;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        _brushCache[c] = brush;
        return brush;
    }

    internal static SolidColorBrush ColorBrushAlpha(Color c, byte alpha)
        => ColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

    // ── judgement色（旧: URBarWindow.xaml.cs / URBarRenderer.cs の GetColor を集約・改名） ──
    // 提案文書では既存名 `GetColor` のままの移設を想定していたが、汎用色ユーティリティである
    // ColorUtils 内では意味が広すぎて紛らわしいため、判定(judgement)専用であることが分かる
    // `JudgementColor` に改名した。呼び出し元の置き換えのみで、返す色・既定値は変更していない。
    internal static Color JudgementColor(int judgement) => judgement switch
    {
        1 => Color.FromRgb(50, 188, 200),
        2 => Color.FromRgb(255, 200, 50),
        3 => Color.FromRgb(50, 200, 100),
        4 => Color.FromRgb(50, 100, 255),
        5 => Color.FromRgb(200, 100, 255),
        _ => Color.FromRgb(150, 150, 150),
    };
}
