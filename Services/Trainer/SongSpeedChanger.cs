using NAudio.Lame;
using NAudio.Vorbis;
using NAudio.Wave;
using SoundTouch;
using SoundTouch.Net.NAudioSupport;
using System.IO;

namespace OsuMate.Services.Trainer
{
    /// <summary>
    /// NAudio + SoundTouch.Net（SoundTouch.Net.NAudioSupport）を使って音声ファイルの再生速度を変換する。
    /// </summary>
    internal static class SongSpeedChanger
    {
        /// <param name="inFile">変換元ファイルパス（.mp3 / .ogg / .wav）</param>
        /// <param name="outFile">出力ファイルパス（.mp3）</param>
        /// <param name="effectiveMultiplier">再生速度倍率</param>
        /// <param name="adjustPitchWithSpeed">
        ///   true  = "Adjust Pitch with Speed"（テープ再生風。SoundTouchProcessor の RateChange を使い
        ///            テンポ・ピッチを同時に変化させる。WSOLA 非使用のため音質劣化が少ない）
        ///   false = テンポのみ変化・ピッチ保持（SoundTouchProcessor の TempoChange を使用）
        /// </param>
        public static void GenerateAudioFile(
            string inFile,
            string outFile,
            decimal effectiveMultiplier,
            bool adjustPitchWithSpeed = false)
        {
            string ext   = Path.GetExtension(inFile).ToLowerInvariant();
            string temp1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext);
            string temp2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");

            try
            {
                File.Copy(inFile, temp1, overwrite: true);

                // mp3 / ogg / wav → wav（PCM）
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

                // SoundTouchProcessor でテンポ・ピッチを変換
                // RateChange  : テンポ+ピッチを同時に変化（テープ再生風、WSOLA不使用で高品質）
                // TempoChange : テンポのみ変化・ピッチ保持（DT/HT風、WSOLA使用）
                double pct = ((double)effectiveMultiplier - 1.0) * 100.0;

                using var wavReader   = new WaveFileReader(temp2);
                using var floatStream = new WaveChannel32(wavReader) { PadWithZeroes = false };

                var processor = new SoundTouchProcessor
                {
                    SampleRate = wavReader.WaveFormat.SampleRate,
                    Channels   = wavReader.WaveFormat.Channels,
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
                using var pcm16Stream      = new Wave32To16Stream(soundTouchStream);

                // wav → mp3（STANDARD 品質）
                if (File.Exists(outFile)) File.Delete(outFile);
                using (var mp3Writer = new LameMP3FileWriter(outFile, pcm16Stream.WaveFormat, LAMEPreset.STANDARD))
                {
                    pcm16Stream.CopyTo(mp3Writer);
                }
            }
            finally
            {
                // 途中で例外が発生した場合でも一時ファイルを残さないようにする
                try { if (File.Exists(temp1)) File.Delete(temp1); } catch { }
                try { if (File.Exists(temp2)) File.Delete(temp2); } catch { }
            }
        }
    }
}
