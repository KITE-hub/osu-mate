using System.IO;
using System.Windows.Media;

namespace OsuMate.Utils
{
    /// <summary>
    /// アプリに同梱（埋め込み）しているフォント（Resources/Fonts 配下）を解決するためのヘルパー。
    /// </summary>
    public static class AppFonts
    {
        /// <summary>アプリに同梱している埋め込みフォントのFamily名一覧（UI上のフォント選択に表示する）。</summary>
        public static readonly IReadOnlyList<string> EmbeddedFontNames = new[] { "Oxanium", "Roboto" };

        /// <summary>
        /// SettingsView のフォント選択一覧で埋め込みフォント（<see cref="EmbeddedFontNames"/>）と
        /// インストール済みシステムフォントの間に区切り線を表示
        /// </summary>
        public const string FontListSeparator = "\uE000";

        private static readonly Uri PackApplicationBaseUri = new("pack://application:,,,/");

        /// <summary>
        /// フォント名から、埋め込みフォントを指す相対パス（pack URIのベースからの相対パス＋フラグメント）
        /// を返す。埋め込みフォントでなければ null。
        /// </summary>
        private static string? ResolveEmbeddedRelativePath(string fontFamilyName) => fontFamilyName switch
        {
            "Oxanium" => "./Resources/Fonts/Oxanium/#Oxanium",
            "Roboto" => "./Resources/Fonts/Roboto/#Roboto",
            _ => null,
        };

        /// <summary>
        /// 埋め込みフォントの実ファイル（Regular ウェイト）1本を指す実行ファイルからの相対パス
        /// </summary>
        private static string? ResolveEmbeddedFileRelativePath(string fontFamilyName) => fontFamilyName switch
        {
            "Oxanium" => "Resources/Fonts/Oxanium/Oxanium-Regular.ttf",
            "Roboto" => "Resources/Fonts/Roboto/Roboto-Regular.ttf",
            _ => null,
        };

        /// <summary>
        /// フォント名から、OxyPlotなど「フォント名を文字列でしか受け取れない先」に渡すための解決済み文字列を返す
        /// </summary>
        public static string ResolveFontFamilyString(string fontFamilyName)
        {
            var relativeFilePath = ResolveEmbeddedFileRelativePath(fontFamilyName);
            if (relativeFilePath is null)
                return fontFamilyName;

            var fullPath = Path.Combine(AppContext.BaseDirectory, relativeFilePath);
            return new Uri(fullPath).AbsoluteUri + "#" + fontFamilyName;
        }

        /// <summary>
        /// 実際に使用する <see cref="FontFamily"/> を解決する。
        /// 埋め込みフォントは pack URI のベースURI付き2引数コンストラクタで解決し、
        /// それ以外はそのままシステムフォント名として解決する
        /// </summary>
        public static FontFamily Resolve(string fontFamilyName)
        {
            var relativePath = ResolveEmbeddedRelativePath(fontFamilyName);
            return relativePath is null
                ? new FontFamily(fontFamilyName)
                : new FontFamily(PackApplicationBaseUri, relativePath);
        }
    }
}
