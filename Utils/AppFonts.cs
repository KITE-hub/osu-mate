using System.IO;
using System.Windows.Media;

namespace OsuMate.Utils
{
  public static class AppFonts
  {
    public static readonly IReadOnlyList<string> EmbeddedFontNames = new[] { "Oxanium", "Roboto" };

    public const string FontListSeparator = "\uE000";

    private static readonly Uri PackApplicationBaseUri = new("pack://application:,,,/");

    private static string? ResolveEmbeddedRelativePath(string fontFamilyName) =>
      fontFamilyName switch
      {
        "Oxanium" => "./Resources/Fonts/Oxanium/#Oxanium",
        "Roboto" => "./Resources/Fonts/Roboto/#Roboto",
        _ => null,
      };

    private static string? ResolveEmbeddedFileRelativePath(string fontFamilyName) =>
      fontFamilyName switch
      {
        "Oxanium" => "Resources/Fonts/Oxanium/Oxanium-Regular.ttf",
        "Roboto" => "Resources/Fonts/Roboto/Roboto-Regular.ttf",
        _ => null,
      };

    public static string ResolveFontFamilyString(string fontFamilyName)
    {
      var relativeFilePath = ResolveEmbeddedFileRelativePath(fontFamilyName);
      if (relativeFilePath is null)
        return fontFamilyName;

      var fullPath = Path.Combine(AppContext.BaseDirectory, relativeFilePath);
      return new Uri(fullPath).AbsoluteUri + "#" + fontFamilyName;
    }

    public static FontFamily Resolve(string fontFamilyName)
    {
      var relativePath = ResolveEmbeddedRelativePath(fontFamilyName);
      return relativePath is null
        ? new FontFamily(fontFamilyName)
        : new FontFamily(PackApplicationBaseUri, relativePath);
    }
  }
}
