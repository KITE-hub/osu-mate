namespace OsuMate.Services.Osu
{
    /// <summary>
    /// Log絞り込み・Best pp算出で共通して使う「対象プレイヤー判定」。
    /// PlayerName が空文字（未ログインで識別名が一切無いプレイ）は、対象プレイヤー名リストに
    /// 何も登録していなくても常に対象に含める。
    /// "Guest"（osu!のローカルユーザー名オプション未設定時のデフォルト値）や、
    /// ユーザー独自のローカル名・オンラインのユーザー名は、通常の名前として
    /// 対象プレイヤー名リストとの一致判定を行う（大文字小文字は区別しない）。
    /// </summary>
    internal static class TargetPlayerFilter
    {
        public static bool Matches(string? playerName, IEnumerable<string> targetPlayerNames)
        {
            if (string.IsNullOrEmpty(playerName)) return true;
            return targetPlayerNames.Contains(playerName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
