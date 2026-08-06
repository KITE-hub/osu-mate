using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// プレイ履歴から「日ごとの合計打数（Miss判定を除く）」を集計する、状態を持たない集計サービス。
    /// コントリビューショングラフ（ContributionGraphViewModel）から利用される。
    /// 責務はあくまで「集計」だけに留め、期間の絞り込みや週単位への整形・色分けレベルの算出
    /// といった表示用の加工は行わない（それらは呼び出し側のViewModelの責務とする）。
    /// </summary>
    public class PlayLogAggregationService
    {
        /// <summary>
        /// 日付（時刻切り捨て）ごとに、Miss判定以外の合計打数
        /// （Count300 + Count100 + Count50 + CountGeki + CountKatu）を合算して返す。
        /// entries に含まれない日付はキーとして含まれない（0件として扱うかどうかは呼び出し側の責務）。
        /// </summary>
        public IReadOnlyDictionary<DateOnly, int> AggregateDailyHits(IEnumerable<PlayLogEntry> entries)
        {
            var result = new Dictionary<DateOnly, int>();

            foreach (var entry in entries)
            {
                var date = DateOnly.FromDateTime(entry.PlayedAt);
                var hits = entry.Count300 + entry.Count100 + entry.Count50 + entry.CountGeki + entry.CountKatu;

                result[date] = result.GetValueOrDefault(date) + hits;
            }

            return result;
        }
    }
}
