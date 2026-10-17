namespace OsuMate.Services.Key
{

  internal interface IOsuKeyConfigProvider
  {
    KeyConfigSnapshot Current { get; }

    bool Refresh(bool isActiveSession);
  }
}
