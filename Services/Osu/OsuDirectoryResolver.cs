using System.IO;

namespace OsuMate.Services.Osu
{

  internal sealed class OsuDirectoryResolver
  {
    private readonly object _lock = new();

    internal string ManualOsuDirectory { get; private set; } = string.Empty;
    internal string OsuDirectory { get; private set; } = string.Empty;
    internal string SongsPath { get; private set; } = string.Empty;
    internal bool IsDirectoryLoaded { get; private set; }

    internal event Action<string>? OnDirectoryLoaded;

    private const int ResolveRetryIntervalMs = 500;

    private long _retryNotBeforeMs;
    private string _lastDetectedPath = string.Empty;

    internal void SetManualDirectory(string directory)
    {
      lock (_lock)
      {
        ManualOsuDirectory = directory?.Trim() ?? string.Empty;
        Volatile.Write(ref _retryNotBeforeMs, 0);
        IsDirectoryLoaded = false;
        OsuDirectory = string.Empty;
        SongsPath = string.Empty;
      }
    }

    internal void TryResolve(string detectedOsuPath)
    {
      if (IsDirectoryLoaded)
        return;

      long now = Environment.TickCount64;
      if (detectedOsuPath == _lastDetectedPath && now < Volatile.Read(ref _retryNotBeforeMs))
        return;
      _lastDetectedPath = detectedOsuPath;

      string? resolvedDir = null;
      lock (_lock)
      {
        if (!string.IsNullOrWhiteSpace(ManualOsuDirectory) && Directory.Exists(ManualOsuDirectory))
          resolvedDir = ManualOsuDirectory;
        else if (!string.IsNullOrEmpty(detectedOsuPath) && Directory.Exists(detectedOsuPath))
          resolvedDir = detectedOsuPath;
      }

      if (resolvedDir == null)
      {
        Volatile.Write(ref _retryNotBeforeMs, now + ResolveRetryIntervalMs);
        return;
      }

      lock (_lock)
      {
        if (IsDirectoryLoaded)
          return;
        OsuDirectory = resolvedDir;
        SongsPath = OsuUtils.GetSongsFolderLocation(OsuDirectory, string.Empty);
        IsDirectoryLoaded = true;
      }

      OnDirectoryLoaded?.Invoke(resolvedDir);
    }
  }
}
