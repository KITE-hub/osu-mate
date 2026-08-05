using NAudio.Lame;
using NAudio.Vorbis;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OsuMate.Services.Trainer
{
    /// <summary>
    /// NAudio + soundstretch.exe を使って音声ファイルの再生速度を変換する。
    /// </summary>
    internal static class SongSpeedChanger
    {
        /// <summary>soundstretch.exe の実行タイムアウト（ミリ秒）。この時間を超えるとプロセスを強制終了する。</summary>
        private const int SoundstretchTimeoutMs = 5 * 60 * 1000; // 5分

        /// <param name="inFile">変換元ファイルパス（.mp3 / .ogg / .wav）</param>
        /// <param name="outFile">出力ファイルパス（.mp3）</param>
        /// <param name="effectiveMultiplier">再生速度倍率</param>
        /// <param name="adjustPitchWithSpeed">
        ///   true  = "Adjust Pitch with Speed"（テープ再生風。soundstretch の -rate フラグを使い
        ///            テンポ・ピッチを同時に変化させる。WSOLA 非使用のため音質劣化が少ない）
        ///   false = テンポのみ変化・ピッチ保持（soundstretch の -tempo フラグ）
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
            string temp3 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");

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

                // soundstretch.exe で変換
                // -rate  : テンポ+ピッチを同時に変化（テープ再生風、WSOLA不使用で高品質）
                // -tempo : テンポのみ変化・ピッチ保持（DT/HT風、WSOLA使用）
                double pct = ((double)effectiveMultiplier - 1.0) * 100.0;
                string ssArgs = adjustPitchWithSpeed
                    ? $"-rate={pct:F4}"
                    : $"-tempo={pct:F4}";

                string soundstretchPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "binaries", "soundstretch.exe");

                if (!File.Exists(soundstretchPath))
                {
                    throw new FileNotFoundException(
                        $"soundstretch.exe not found: {soundstretchPath}", soundstretchPath);
                }

                var stderrBuilder = new StringBuilder();

                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName               = soundstretchPath,
                        Arguments              = $"\"{temp2}\" \"{temp3}\" {ssArgs}",
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                    }
                })
                {
                    // 標準出力・標準エラーの両方を非同期で読み捨てる（読み取らないと
                    // OSパイプが一杯になった際に子プロセスがブロックし、WaitForExitと
                    // 合わせてデッドロックする恐れがあるため）。エラーはメッセージ用に保持する。
                    proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };
                    proc.OutputDataReceived += (_, _) => { };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    if (!proc.WaitForExit(SoundstretchTimeoutMs))
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        throw new TimeoutException(
                            $"soundstretch.exe timed out ({SoundstretchTimeoutMs / 1000}s). Operation aborted.");
                    }

                    if (proc.ExitCode != 0)
                    {
                        string detail = stderrBuilder.Length > 0 ? stderrBuilder.ToString().Trim() : "(No details)";
                        throw new InvalidOperationException(
                            $"soundstretch.exe exited with error (ExitCode={proc.ExitCode}): {detail}");
                    }
                }

                // wav → mp3（STANDARD 品質）
                if (File.Exists(outFile)) File.Delete(outFile);
                using (var wavReader = new WaveFileReader(temp3))
                using (var mp3Writer = new LameMP3FileWriter(outFile, wavReader.WaveFormat, LAMEPreset.STANDARD))
                {
                    wavReader.CopyTo(mp3Writer);
                }
            }
            finally
            {
                // 途中で例外が発生した場合でも一時ファイルを残さないようにする
                try { if (File.Exists(temp1)) File.Delete(temp1); } catch { }
                try { if (File.Exists(temp2)) File.Delete(temp2); } catch { }
                try { if (File.Exists(temp3)) File.Delete(temp3); } catch { }
            }
        }
    }
}
