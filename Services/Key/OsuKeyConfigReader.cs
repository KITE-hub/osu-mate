using System.IO;
using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.Key
{

  internal sealed class OsuKeyConfigReader(Func<string> osuDirectory)
  {
    private static readonly TimeSpan KeyConfigRecheckInterval = TimeSpan.FromSeconds(1);
    private readonly object _keyConfigLock = new();
    private string _keyConfigPath = string.Empty;
    private DateTime _keyConfigLastWriteTimeUtc;
    private DateTime _keyConfigLastCheckedUtc = DateTime.MinValue;
    private Dictionary<string, string> _keyConfigValues = [];
    private bool _keyConfigReloading;
    private volatile bool _keyConfigDirty;

    internal string[] ReadManiaLayout(int keyCount)
    {
      var prefix = $"ManiaLayouts{keyCount}K";
      lock (_keyConfigLock)
      {
        return _keyConfigValues.TryGetValue(prefix, out var value)
          ? value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          : [];
      }
    }

    internal string GetConfiguredKeyName(string keyName, string fallback)
    {
      lock (_keyConfigLock)
        return _keyConfigValues.TryGetValue(keyName, out var value) && !string.IsNullOrWhiteSpace(value)
          ? value
          : fallback;
    }

    internal bool Refresh(bool isActiveSession)
    {
      if (isActiveSession)
        return ConsumeKeyConfigDirty();

      var now = DateTime.UtcNow;
      if (now - _keyConfigLastCheckedUtc >= KeyConfigRecheckInterval)
        RequestKeyConfigReload(now);

      return ConsumeKeyConfigDirty();
    }

    private bool ConsumeKeyConfigDirty()
    {
      if (!_keyConfigDirty)
        return false;
      _keyConfigDirty = false;
      return true;
    }

    private void RequestKeyConfigReload(DateTime now)
    {
      lock (_keyConfigLock)
      {
        if (_keyConfigReloading)
          return;
        _keyConfigReloading = true;
        _keyConfigLastCheckedUtc = now;
      }

      Task.Run(ReloadKeyConfig);
    }

    private void ReloadKeyConfig()
    {
      try
      {
        var path = FindKeyConfigPath();
        if (!File.Exists(path))
        {
          lock (_keyConfigLock)
          {
            var hadValues = _keyConfigValues.Count > 0 || !string.IsNullOrEmpty(_keyConfigPath);
            _keyConfigPath = string.Empty;
            _keyConfigLastWriteTimeUtc = default;
            _keyConfigValues = [];
            if (hadValues)
              _keyConfigDirty = true;
          }
          return;
        }

        DateTime lastWriteTimeUtc;
        try
        {
          lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger($"KeyOverlayInputService.ReloadKeyConfig failed: {e}", true);
          return;
        }

        lock (_keyConfigLock)
        {
          if (string.Equals(_keyConfigPath, path, StringComparison.Ordinal)
              && _keyConfigLastWriteTimeUtc == lastWriteTimeUtc)
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
          foreach (var line in File.ReadLines(path))
          {
            var separator = line.IndexOf('=');
            if (separator <= 0)
              continue;

            var key = line[..separator].Trim();
            if (key.StartsWith("ManiaLayouts", StringComparison.Ordinal)
                || key.StartsWith("keyOsu", StringComparison.Ordinal)
                || key.StartsWith("keyTaiko", StringComparison.Ordinal)
                || key.StartsWith("keyFruits", StringComparison.Ordinal))
              values[key] = line[(separator + 1)..].Trim();
          }

          lock (_keyConfigLock)
          {
            _keyConfigPath = path;
            _keyConfigLastWriteTimeUtc = lastWriteTimeUtc;
            _keyConfigValues = values;
          }
          _keyConfigDirty = true;
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger($"KeyOverlayInputService.ReloadKeyConfig failed: {e}", true);
        }
      }
      finally
      {
        lock (_keyConfigLock)
          _keyConfigReloading = false;
      }
    }

    private string FindKeyConfigPath()
    {
      try
      {
        return OsuUtils.FindUserConfigPath(osuDirectory()) ?? string.Empty;
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"KeyOverlayInputService.FindKeyConfigPath failed: {e}", true);
        return string.Empty;
      }
    }

    internal static bool TryParseKey(string name, out Keys key)
    {
      if (TryParseDefinedKey(name, out key))
        return true;

      var normalized = name switch
      {
        "LeftControl" => "LControlKey",
        "RightControl" => "RControlKey",
        "LeftShift" => "LShiftKey",
        "RightShift" => "RShiftKey",
        "LeftAlt" => "LMenu",
        "RightAlt" => "RMenu",
        "LeftWindows" => "LWin",
        "RightWindows" => "RWin",
        "Backspace" => "Back",
        "Enter" => "Return",
        _ => name,
      };

      return TryParseDefinedKey(normalized, out key);
    }

    private static bool TryParseDefinedKey(string name, out Keys key) =>
      Enum.TryParse(name, true, out key) && key != Keys.None && Enum.IsDefined(typeof(Keys), key);
  }
}
