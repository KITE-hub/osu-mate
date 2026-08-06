using System;
using System.Linq;
using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// 状態を持たない、キー生成・整形ロジックをまとめたもの。
    /// scores.db 由来のエントリとメモリ由来のエントリで必ず同じ式を使う必要があるため、
    /// PlayLog 各クラスから共通で呼び出す。
    /// </summary>
    public static class PlayLogKeyBuilder
    {
        /// <summary>
        /// scores.db の Timestamp は Windows/.NET の Ticks 値そのものだが、実体は
        /// (osu!本体が DateTime.Now.Ticks で書き出しているため)ローカル時刻。
        /// これを DateTimeKind.Utc として扱うと、メモリ側(DateTime.Now = Local)の PlayedAt と
        /// 9時間ズレる(JST環境の場合)。Kind は必ず Local に揃えること。
        /// </summary>
        public static DateTime FileTimeToLocal(long ticks)
        {
            return new DateTime(ticks, DateTimeKind.Local);
        }

        /// <summary>
        /// 完走プレイの結合キー。scores.db 由来のエントリもメモリ由来(仮登録)のエントリも
        /// 必ずこの式で計算する。時刻を使わないのは、プレイ開始時刻(メモリ側)とスコア確定時刻
        /// (scores.db 側)が譜面の再生時間ぶんズレてしまい、時刻ベースでは突き合わせられないため。
        /// </summary>
        public static string MakeCompletedJoinKey(string beatmapMd5, string playerName, int modsRaw, int totalScore)
        {
            if (playerName == "Guest") playerName = "";
            return $"cj|{beatmapMd5}|{playerName}|{modsRaw}|{totalScore}";
        }

        /// <summary>中断プレイ用のキー。scores.db に対応物が来ないため、開始時刻ベースのままでよい。</summary>
        public static string MakeInterruptedKey(PlayLogEntry e)
        {
            var playerName = e.PlayerName == "Guest" ? "" : e.PlayerName;
            return $"mem|{e.BeatmapMd5}|{playerName}|{e.PlayedAt:yyyyMMddHHmmssfff}";
        }

        /// <summary>生の Mods ビットマスクを表示用文字列("HD,DT" / "NM")に整形する。</summary>
        public static string FormatModsString(int modsRaw)
        {
            var calc = OsuUtils.ParseMods(modsRaw).Calculation;
            return calc.Length == 0 ? "NM" : string.Join(",", calc.Select(m => m.ToUpper()));
        }
    }
}
