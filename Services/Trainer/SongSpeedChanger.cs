using System.IO;
using NAudio.Lame;
using NAudio.Vorbis;
using NAudio.Wave;
using SoundTouch;
using SoundTouch.Net.NAudioSupport;

namespace OsuMate.Services.Trainer
{
  internal static class SongSpeedChanger
  {
    public static void GenerateAudioFile(
      string inFile,
      string outFile,
      decimal effectiveMultiplier,
      bool adjustPitchWithSpeed = false
    )
    {
      string ext = Path.GetExtension(inFile).ToLowerInvariant();
      var owned = new List<IDisposable>();
      string? tempWavPath = null;

      try
      {
        var input = Own(
          new FileStream(
            inFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
          ),
          owned
        );

        var source = OpenPcmSource(ext, input, owned, out tempWavPath);

        double pct = ((double)effectiveMultiplier - 1.0) * 100.0;

        var floatStream = Own(new WaveChannel32(source) { PadWithZeroes = false }, owned);

        var processor = new SoundTouchProcessor
        {
          SampleRate = source.WaveFormat.SampleRate,
          Channels = source.WaveFormat.Channels,
        };

        if (adjustPitchWithSpeed)
        {
          processor.RateChange = pct;
        }
        else
        {
          processor.TempoChange = pct;
        }

        var soundTouchStream = Own(new SoundTouchWaveStream(floatStream, processor), owned);
        var pcm16Stream = Own(new Wave32To16Stream(soundTouchStream), owned);

        string outputTempFile = outFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
          using (
            var mp3Writer = new LameMP3FileWriter(
              outputTempFile,
              pcm16Stream.WaveFormat,
              LAMEPreset.STANDARD
            )
          )
          {
            pcm16Stream.CopyTo(mp3Writer);
          }
          File.Move(outputTempFile, outFile, overwrite: true);
        }
        finally
        {
          if (File.Exists(outputTempFile))
            File.Delete(outputTempFile);
        }
      }
      finally
      {
        for (int i = owned.Count - 1; i >= 0; i--)
          owned[i].Dispose();

        if (tempWavPath != null)
          TryDeleteFile(tempWavPath);
      }
    }

    private static WaveStream OpenPcmSource(
      string ext,
      Stream input,
      List<IDisposable> owned,
      out string? tempWavPath
    )
    {
      tempWavPath = null;
      switch (ext)
      {
        case ".mp3":
          return Own(new Mp3FileReader(input), owned);

        case ".wav":
          var wavIn = Own(new WaveFileReader(input), owned);
          return wavIn.WaveFormat.Encoding == NAudio.Wave.WaveFormatEncoding.Pcm
            ? wavIn
            : Own(WaveFormatConversionStream.CreatePcmStream(wavIn), owned);

        case ".ogg":
          tempWavPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
          using (var vorbis = new VorbisWaveReader(input))
          {
            WaveFileWriter.CreateWaveFile(tempWavPath, vorbis.ToWaveProvider16());
          }
          return Own(new WaveFileReader(tempWavPath), owned);

        default:
          throw new NotSupportedException($"Unsupported audio format: {ext}");
      }
    }

    private static T Own<T>(T disposable, List<IDisposable> owned)
      where T : IDisposable
    {
      owned.Add(disposable);
      return disposable;
    }

    private static void TryDeleteFile(string path)
    {
      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch (Exception ex)
      {
        OsuMate.Utils.LogUtils.DebugLogger($"SongSpeedChanger: failed to delete {path}: {ex}", true);
      }
    }
  }
}
