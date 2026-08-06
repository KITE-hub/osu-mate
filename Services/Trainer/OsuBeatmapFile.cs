using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OsuMate.Services.Trainer
{
    /// <summary>
    /// .osu ファイルの最小限パーサー／ライター。
    /// Rate 変更・難易度スケーリング・osutrainer タグ検出に対応。
    /// </summary>
    internal class OsuBeatmapFile
    {
        // ---------- メタデータ ----------
        public string Filename      { get; private set; } = "";
        public string Title         { get; private set; } = "";
        public string Artist        { get; private set; } = "";
        public string Creator       { get; private set; } = "";
        public string Version       { get; set; } = "";
        public string AudioFilename { get; set; } = "";
        public int    BeatmapID     { get; private set; } = -1;
        public List<string> Tags    { get; set; } = [];

        /// <summary>
        /// osutrainer タグが含まれている場合 true。
        /// 生成済み譜面への二重 Rate 適用を防ぐために使用する。
        /// </summary>
        public bool IsOsuTrainerMap => Tags.Contains("osutrainer");

        /// <summary>
        /// この譜面が osutrainer によって生成されたものである場合、
        /// 生成元（元譜面）の .osu ファイル名（拡張子込み、パスなし）。
        /// SaveWithRate 時にコメント行として埋め込まれ、FindOriginalMap で
        /// 元譜面を確実に逆引きするために使用する。埋め込みが無い場合は null。
        /// </summary>
        public string? SourceOsuFileName { get; private set; }

        /// <summary>元譜面ファイル名を埋め込むコメント行のプレフィックス。</summary>
        private const string SourceMarkerPrefix = "// osutrainer:source=";

        // ---------- 難易度 ----------
        public decimal ApproachRate      { get; private set; } = -1M;
        public decimal OverallDifficulty { get; private set; } = -1M;
        public decimal HPDrainRate       { get; private set; } = -1M;
        public decimal CircleSize        { get; private set; } = -1M;

        // ---------- ゲームモード ----------
        /// <summary>
        /// [General] Mode の値。0=osu!, 1=taiko, 2=catch, 3=mania。
        /// taiko/mania では AR は使われず、CS はキー数（mania）や無意味な値（taiko）を
        /// 表すに過ぎないため、Trainer 側で AR/CS の編集可否判定に使用する。
        /// </summary>
        public int Mode { get; private set; } = 0;

        /// <summary>taiko(1) または mania(3) の場合 true。AR/CS がゲームプレイに影響しないモード。</summary>
        public bool IsTaikoOrMania => Mode == 1 || Mode == 3;

        // ---------- BPM ----------
        public decimal DominantBpm { get; private set; }
        public decimal MinBpm      { get; private set; }
        public decimal MaxBpm      { get; private set; }

        // ---------- 内部生データ ----------
        private List<string> _rawLines = [];

        private enum Section
        {
            None, General, Metadata, Difficulty,
            Events, TimingPoints, HitObjects, Editor, Other
        }

        private OsuBeatmapFile() { }

        // ============================================================
        //  静的ファクトリ
        // ============================================================

        public static OsuBeatmapFile Load(string filePath)
        {
            var bm = new OsuBeatmapFile { Filename = filePath };
            bm._rawLines = File.ReadAllLines(filePath, Encoding.UTF8).ToList();
            bm.Parse();
            return bm;
        }

        // ============================================================
        //  パース
        // ============================================================

        private void Parse()
        {
            var section = Section.None;
            var bpmPoints = new List<(double time, double beatLength)>();
            double lastHitObjectTime = 0;

            foreach (var raw in _rawLines)
            {
                var line = raw.Trim();
                if (line.StartsWith(SourceMarkerPrefix, StringComparison.Ordinal))
                    SourceOsuFileName = line[SourceMarkerPrefix.Length..].Trim();
                if (line.StartsWith("//") || line == "") continue;

                if (line.StartsWith('['))
                {
                    section = line switch
                    {
                        "[General]"      => Section.General,
                        "[Metadata]"     => Section.Metadata,
                        "[Difficulty]"   => Section.Difficulty,
                        "[Events]"       => Section.Events,
                        "[TimingPoints]" => Section.TimingPoints,
                        "[HitObjects]"   => Section.HitObjects,
                        _                => Section.Other
                    };
                    continue;
                }

                switch (section)
                {
                    case Section.General:
                    case Section.Metadata:
                    case Section.Difficulty:
                        ParseHeaderSectionLine(section, line);
                        break;

                    case Section.TimingPoints:
                        ParseTimingPoint(line, bpmPoints);
                        break;

                    case Section.HitObjects:
                        ParseHitObjectTime(line, ref lastHitObjectTime);
                        break;
                }
            }

            CalcBpm(bpmPoints, lastHitObjectTime);
        }

        /// <summary>
        /// [General]/[Metadata]/[Difficulty] セクション内の1行をパースしてプロパティへ反映する。
        /// フルパース（<see cref="Parse"/>）とメタデータ専用パース（<see cref="ParseMetadataOnly"/>）の
        /// 両方から共有される（旧: <see cref="Services.PlayLog.BeatmapPathResolver"/> 側に同種のロジックが
        /// 独立して重複実装されていたが、統合によりここへ一本化した）。
        /// </summary>
        private void ParseHeaderSectionLine(Section section, string line)
        {
            switch (section)
            {
                case Section.General:
                    if (TryKV(line, "AudioFilename", out var af)) AudioFilename = af;
                    if (TryKV(line, "Mode", out var modeStr) &&
                        int.TryParse(modeStr, out int mode))     Mode = mode;
                    break;

                case Section.Metadata:
                    if (TryKV(line, "Title",     out var title))   Title    = title;
                    if (TryKV(line, "Artist",    out var artist))  Artist   = artist;
                    if (TryKV(line, "Creator",   out var creator)) Creator  = creator;
                    if (TryKV(line, "Version",   out var version)) Version  = version;
                    if (TryKV(line, "BeatmapID", out var bidStr) &&
                        int.TryParse(bidStr, out int bid))          BeatmapID = bid;
                    if (TryKV(line, "Tags", out var tags))
                        Tags = [.. tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
                    break;

                case Section.Difficulty:
                    if (TryKVDecimal(line, "ApproachRate",      out var ar)) ApproachRate      = ar;
                    if (TryKVDecimal(line, "OverallDifficulty", out var od)) OverallDifficulty = od;
                    if (TryKVDecimal(line, "HPDrainRate",       out var hp)) HPDrainRate       = hp;
                    if (TryKVDecimal(line, "CircleSize",        out var cs)) CircleSize        = cs;
                    break;
            }
        }

        // ============================================================
        //  メタデータ専用の軽量パース（PlayLog側から利用）
        // ============================================================

        /// <summary>
        /// [General]/[Metadata]/[Difficulty] セクションのみを読み取る軽量ファクトリ。
        /// TimingPoints/HitObjectsの走査やBPM計算を行わず、[Difficulty]セクションを
        /// 読み終えた時点でファイル読み込みを打ち切るため、フルパースの <see cref="Load"/> より高速。
        /// 譜面の Artist/Title/Version(難易度名)/Creator/BeatmapID/CircleSize 等、
        /// メタデータだけが必要な場面（PlayLog記録時のスナップショット取得等）向け。
        /// </summary>
        public static OsuBeatmapFile LoadMetadataOnly(string filePath)
        {
            var bm = new OsuBeatmapFile { Filename = filePath };
            bm.ParseMetadataOnly(filePath);
            return bm;
        }

        private void ParseMetadataOnly(string filePath)
        {
            var section = Section.None;
            bool reachedDifficultySection = false;

            // Load() と異なり全行をメモリに保持せず、File.ReadLines でストリーミング読み込みする
            // （メタデータ取得のためだけに巨大な HitObjects セクションまで読む必要はないため）。
            foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.StartsWith("//") || line == "") continue;

                if (line.StartsWith('['))
                {
                    // [Difficulty] セクションを読み終えて次のセクション（[Events]等）に入った時点で、
                    // メタデータ取得に必要な情報は揃っているため、以降は読み込まずに打ち切る。
                    if (reachedDifficultySection) break;

                    section = line switch
                    {
                        "[General]"    => Section.General,
                        "[Metadata]"   => Section.Metadata,
                        "[Difficulty]" => Section.Difficulty,
                        _              => Section.Other
                    };
                    if (section == Section.Difficulty) reachedDifficultySection = true;
                    continue;
                }

                if (section is Section.General or Section.Metadata or Section.Difficulty)
                    ParseHeaderSectionLine(section, line);
            }
        }

        private static bool TryKV(string line, string key, out string value)
        {
            value = "";
            if (!line.StartsWith(key + ":")) return false;
            value = line[(key.Length + 1)..].Trim();
            return true;
        }

        private static bool TryKVDecimal(string line, string key, out decimal value)
        {
            value = 0;
            if (!TryKV(line, key, out var raw)) return false;
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static void ParseTimingPoint(string line, List<(double, double)> bpmPoints)
        {
            var parts = line.Split(',');
            if (parts.Length < 7) return;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)) return;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength)) return;
            if (!int.TryParse(parts[6].Trim(), out int uninherited)) return;
            if (uninherited == 1 && beatLength > 0)
                bpmPoints.Add((time, beatLength));
        }

        private static void ParseHitObjectTime(string line, ref double lastTime)
        {
            var parts = line.Split(',');
            if (parts.Length < 3) return;
            if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                lastTime = Math.Max(lastTime, t);
        }

        private void CalcBpm(List<(double time, double beatLength)> pts, double lastHitObjectTime)
        {
            if (pts.Count == 0) return;
            pts.Sort((a, b) => a.time.CompareTo(b.time));

            var durations = new Dictionary<double, double>();
            for (int i = 0; i < pts.Count; i++)
            {
                double endTime = (i + 1 < pts.Count) ? pts[i + 1].time : Math.Max(pts[i].time, lastHitObjectTime);
                double dur = Math.Max(0, endTime - pts[i].time);
                var bl = pts[i].beatLength;
                if (durations.ContainsKey(bl)) durations[bl] += dur;
                else                           durations[bl]   = dur;
            }

            var dominantBl = durations.OrderByDescending(kv => kv.Value).First().Key;
            DominantBpm = (decimal)(60000.0 / dominantBl);
            MinBpm      = (decimal)(60000.0 / pts.Max(p => p.beatLength));
            MaxBpm      = (decimal)(60000.0 / pts.Min(p => p.beatLength));
        }

        // ============================================================
        //  難易度スケーリングヘルパー（public static で ViewModel からも呼べる）
        // ============================================================

        /// <summary>Rate 適用後の AR 値を計算する（最大 11.0 を許容）。</summary>
        public static decimal ComputeNewAR(decimal ar, decimal rate)
        {
            double arMs = (double)ar <= 5.0
                ? 1800.0 - 120.0 * (double)ar
                : 1200.0 - 150.0 * ((double)ar - 5.0);
            double newArMs = arMs / (double)rate;
            double newAr   = newArMs >= 1200.0
                ? (1800.0 - newArMs) / 120.0
                : 5.0 + (1200.0 - newArMs) / 150.0;
            return (decimal)Math.Clamp(newAr, 0.0, 11.0);
        }

        /// <summary>Rate 適用後の OD 値を計算する（最大 11.0 を許容）。</summary>
        public static decimal ComputeNewOD(decimal od, decimal rate)
        {
            double odMs    = 80.0 - 6.0 * (double)od;
            double newOdMs = odMs / (double)rate;
            double newOd   = (80.0 - newOdMs) / 6.0;
            return (decimal)Math.Clamp(newOd, 0.0, 11.0);
        }

        // ============================================================
        //  Rate 適用 → ファイル保存
        // ============================================================

        /// <summary>
        /// Rate と難易度オーバーライド値を適用した新しい .osu を outputPath に保存する。
        /// null を渡した難易度値は元の値のまま保持される。
        /// </summary>
        public void SaveWithRate(
            string   outputPath,
            decimal  rate,
            decimal? arOverride = null,
            decimal? odOverride = null,
            decimal? hpOverride = null,
            decimal? csOverride = null)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate), rate, "rate must be greater than zero.");

            var outLines = new List<string>();
            var section  = Section.None;

            foreach (var raw in _rawLines)
            {
                var line = raw.Trim();

                if (line.StartsWith('['))
                {
                    section = line switch
                    {
                        "[General]"      => Section.General,
                        "[Metadata]"     => Section.Metadata,
                        "[Difficulty]"   => Section.Difficulty,
                        "[Events]"       => Section.Events,
                        "[TimingPoints]" => Section.TimingPoints,
                        "[HitObjects]"   => Section.HitObjects,
                        "[Editor]"       => Section.Editor,
                        _                => Section.Other
                    };
                    outLines.Add(raw);
                    continue;
                }

                switch (section)
                {
                    case Section.General:
                        if (TryKV(line, "AudioFilename", out _))
                            outLines.Add($"AudioFilename: {AudioFilename}");
                        else if (TryKV(line, "PreviewTime", out var ptStr) &&
                                 int.TryParse(ptStr, out int pt))
                        {
                            // -1 は「プレビュー位置未設定（デフォルト挙動）」を表す特別な値なので、
                            // Rateを掛けて丸めてしまうと 0 に化けてしまう。負値はスケールしない。
                            int newPt = pt < 0 ? pt : (int)Math.Round(pt / (double)rate);
                            outLines.Add($"PreviewTime: {newPt}");
                        }
                        else
                            outLines.Add(raw);
                        break;

                    case Section.Metadata:
                        if (TryKV(line, "Version", out _))
                            outLines.Add($"Version:{Version}");
                        else if (TryKV(line, "Tags", out _))
                            outLines.Add($"Tags:{string.Join(" ", Tags)}");
                        else if (TryKV(line, "BeatmapID", out _))
                            // 生成譜面はオンラインの元譜面とは別物なので、未提出を表す値にリセット
                            outLines.Add("BeatmapID:0");
                        else if (TryKV(line, "BeatmapSetID", out _))
                            outLines.Add("BeatmapSetID:-1");
                        else
                            outLines.Add(raw);
                        break;

                    case Section.Difficulty:
                        outLines.Add(OverrideDifficultyLine(raw, arOverride, odOverride, hpOverride, csOverride));
                        break;

                    case Section.Events:
                        // Video・ストーリーボードは生成後の譜面から除去する（null は「出力しない」の意味）。
                        var eventLine = FilterAndScaleEventLine(raw, rate);
                        if (eventLine != null) outLines.Add(eventLine);
                        break;

                    case Section.TimingPoints:
                        outLines.Add(ScaleTimingPointLine(raw, rate));
                        break;

                    case Section.HitObjects:
                        outLines.Add(ScaleHitObjectLine(raw, rate));
                        break;

                    case Section.Editor:
                        outLines.Add(ScaleBookmarksLine(raw, rate));
                        break;

                    default:
                        outLines.Add(raw);
                        break;
                }
            }

            // 元譜面ファイル名をコメント行として埋め込む。
            string marker = SourceMarkerPrefix + Path.GetFileName(Filename);
            int markerIndex = (outLines.Count > 0 &&
                                outLines[0].TrimStart().StartsWith("osu file format", StringComparison.OrdinalIgnoreCase))
                ? 1 : 0;
            outLines.Insert(markerIndex, marker);

            File.WriteAllLines(outputPath, outLines, Encoding.UTF8);
        }

        // ---- Difficulty 行の明示値オーバーライド ----
        private static string OverrideDifficultyLine(
            string raw, decimal? ar, decimal? od, decimal? hp, decimal? cs)
        {
            var line = raw.Trim();
            if (line == "") return raw;
            if (ar.HasValue && TryKVDecimal(line, "ApproachRate",      out _)) return $"ApproachRate:{ar.Value.ToString("F10", CultureInfo.InvariantCulture)}";
            if (od.HasValue && TryKVDecimal(line, "OverallDifficulty", out _)) return $"OverallDifficulty:{od.Value.ToString("F10", CultureInfo.InvariantCulture)}";
            if (hp.HasValue && TryKVDecimal(line, "HPDrainRate",       out _)) return $"HPDrainRate:{hp.Value.ToString("F10", CultureInfo.InvariantCulture)}";
            if (cs.HasValue && TryKVDecimal(line, "CircleSize",        out _)) return $"CircleSize:{cs.Value.ToString("F10", CultureInfo.InvariantCulture)}";
            return raw;
        }

        // ---- [Editor] Bookmarks 行のスケーリング ----
        // エディタ用の時間マーカー一覧。プレイには影響しないが、生成した譜面を
        // エディタで開いた際に元の位置からずれるのを防ぐためスケールする。
        private static string ScaleBookmarksLine(string raw, decimal rate)
        {
            if (!TryKV(raw.Trim(), "Bookmarks", out var value)) return raw;

            var times = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double t)
                    ? ((int)Math.Round(t / (double)rate)).ToString(CultureInfo.InvariantCulture)
                    : s.Trim());

            return $"Bookmarks: {string.Join(",", times)}";
        }

        // ---- TimingPoint 行のスケーリング ----
        private static string ScaleTimingPointLine(string raw, decimal rate)
        {
            var line = raw.Trim();
            if (line == "" || line.StartsWith("//")) return raw;

            var parts = line.Split(',');
            if (parts.Length < 7) return raw;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)) return raw;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength)) return raw;
            if (!int.TryParse(parts[6].Trim(), out int uninherited)) return raw;

            double newTime       = time / (double)rate;
            double newBeatLength = uninherited == 1 ? beatLength / (double)rate : beatLength;

            parts[0] = ((int)Math.Round(newTime)).ToString(CultureInfo.InvariantCulture);
            parts[1] = newBeatLength.ToString("F10", CultureInfo.InvariantCulture);

            return string.Join(",", parts);
        }

        // ---- HitObject 行のスケーリング ----
        private static string ScaleHitObjectLine(string raw, decimal rate)
        {
            var line = raw.Trim();
            if (line == "" || line.StartsWith("//")) return raw;

            var parts = line.Split(',');
            if (parts.Length < 3) return raw;

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)) return raw;
            parts[2] = ((int)Math.Round(time / (double)rate)).ToString(CultureInfo.InvariantCulture);

            if (parts.Length >= 5 && int.TryParse(parts[3], out int typeFlags))
            {
                bool isSpinner = (typeFlags & 8) != 0;
                bool isHold    = (typeFlags & 128) != 0;

                if (isSpinner && parts.Length >= 6)
                {
                    if (double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double endTime))
                        parts[5] = ((int)Math.Round(endTime / (double)rate)).ToString(CultureInfo.InvariantCulture);
                }
                else if (isHold && parts.Length >= 6)
                {
                    var extra = parts[5].Split(':');
                    if (extra.Length >= 1 && double.TryParse(extra[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double holdEnd))
                    {
                        extra[0] = ((int)Math.Round(holdEnd / (double)rate)).ToString(CultureInfo.InvariantCulture);
                        parts[5] = string.Join(":", extra);
                    }
                }
            }

            return string.Join(",", parts);
        }

        // ---- Events 行のフィルタ／スケーリング ----
        // Video・ストーリーボード（Sprite/Animation とそれにぶら下がるコマンド行）は
        // 生成後の譜面から完全に除去する。動画デコードやストーリーボード描画は
        // 生成にも再生負荷にも時間がかかり、Trainerで求められる軽快さを損なうため。
        // 戻り値が null の場合、その行は出力しない（＝譜面から除去する）。
        //
        // 備考: 別ファイルの .osb（ストーリーボードファイル）自体はこのメソッドの対象外。
        // .osb はセット内の全難易度で共有されるため、.osb が存在する譜面セットでは
        // このメソッドで .osu 側の埋め込みストーリーボードを除去しても、
        // .osb 側のストーリーボードは引き続き適用される点に注意。
        private static string? FilterAndScaleEventLine(string raw, decimal rate)
        {
            var line = raw.Trim();
            if (line == "" || line.StartsWith("//")) return raw;

            // 先頭が空白または '_' のインデントされた行は、Sprite/Animation にぶら下がる
            // ストーリーボードのコマンド行（F,M,S,V,R,C,P,L,T,Trigger 等）なので除去する。
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t' || raw[0] == '_'))
                return null;

            if (line.StartsWith("0,0")) return raw;   // Background 宣言はそのまま維持する

            var parts = line.Split(',');
            if (parts.Length < 2) return raw;

            string eventType = parts[0].Trim();

            // Video は除去する
            if (eventType == "1" || eventType == "Video") return null;

            // ストーリーボードのスプライト／アニメーション宣言も除去する
            if (eventType == "Sprite" || eventType == "Animation") return null;

            // Break はRateに合わせて時間をスケールする
            if ((eventType == "2" || eventType == "Break") && parts.Length >= 3)
            {
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double st))
                    parts[1] = ((int)Math.Round(st / (double)rate)).ToString(CultureInfo.InvariantCulture);
                if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double et))
                    parts[2] = ((int)Math.Round(et / (double)rate)).ToString(CultureInfo.InvariantCulture);
                return string.Join(",", parts);
            }

            // Sample（ヒットサウンド用の音声サンプル再生）は描画負荷が無く軽量なので残し、
            // 時間だけRateに合わせてスケールする。
            if ((eventType == "5" || eventType == "Sample") && parts.Length >= 2)
            {
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double st))
                    parts[1] = ((int)Math.Round(st / (double)rate)).ToString(CultureInfo.InvariantCulture);
                return string.Join(",", parts);
            }

            return raw;
        }

        // ============================================================
        //  ユーティリティ
        // ============================================================

        public static string NormalizeForFilename(string s)
            => Regex.Replace(s, @"[""*\\/?\<>|:]", "");
    }
}
