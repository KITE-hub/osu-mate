namespace OsuMate.Models
{

  internal static class OsuMode
  {
    internal const int Osu = 0;
    internal const int Taiko = 1;
    internal const int Catch = 2;
    internal const int Mania = 3;

    internal const int ManiaMinKeyCount = 1;
    internal const int ManiaMaxKeyCount = 18;

    internal static bool IsValid(int mode) => mode is >= Osu and <= Mania;
  }
}
