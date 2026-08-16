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
      string temp1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext);
      string temp2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");

      try
      {
        File.Copy(inFile, temp1, overwrite: true);

        if (ext == ".mp3")
        {
          using var mp3 = new Mp3FileReader(temp1);
          using var wav = WaveFormatConversionStream.CreatePcmStream(mp3);
          WaveFileWriter.CreateWaveFile(temp2, wav);
        }
        else if (ext == ".ogg")
        {
          using var vorbis = new VorbisWaveReader(temp1);
          WaveFileWriter.CreateWaveFile(temp2, vorbis.ToWaveProvider16());
        }
        else if (ext == ".wav")
        {
          using var wavIn = new WaveFileReader(temp1);
          if (wavIn.WaveFormat.Encoding == NAudio.Wave.WaveFormatEncoding.Pcm)
          {
            WaveFileWriter.CreateWaveFile(temp2, wavIn);
          }
          else
          {
            using var pcm = WaveFormatConversionStream.CreatePcmStream(wavIn);
            WaveFileWriter.CreateWaveFile(temp2, pcm);
          }
        }
        else
        {
          throw new NotSupportedException($"Unsupported audio format: {ext}");
        }

        double pct = ((double)effectiveMultiplier - 1.0) * 100.0;

        using var wavReader = new WaveFileReader(temp2);
        using var floatStream = new WaveChannel32(wavReader) { PadWithZeroes = false };

        var processor = new SoundTouchProcessor
        {
          SampleRate = wavReader.WaveFormat.SampleRate,
          Channels = wavReader.WaveFormat.Channels,
        };

        if (adjustPitchWithSpeed)
        {
          processor.RateChange = pct;
        }
        else
        {
          processor.TempoChange = pct;
        }

        using var soundTouchStream = new SoundTouchWaveStream(floatStream, processor);
        using var pcm16Stream = new Wave32To16Stream(soundTouchStream);

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
        try
        {
          if (File.Exists(temp1))
            File.Delete(temp1);
        }
        catch { }
        try
        {
          if (File.Exists(temp2))
            File.Delete(temp2);
        }
        catch { }
      }
    }
  }
}
