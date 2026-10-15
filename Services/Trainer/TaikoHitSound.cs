namespace OsuMate.Services.Trainer
{
  internal static class TaikoHitSound
  {
    internal const int Normal = 1;
    internal const int Whistle = 2;
    internal const int Finish = 4;

    internal const int Don = Normal;
    internal const int Kat = Normal | Whistle;
  }
}
