using System;
using System.Collections.Generic;

namespace OsuMate.Services.Osu;

/// <summary>
/// HitErrorsに対するUR・offset平均用の統計量を、Welfordのオンラインアルゴリズムで増分更新するアキュムレータ。
/// 
/// - 過去の計算済み件数との差分のみを取り込むことで、リストの再走査コストを抑える。
/// - 複数のスレッドから同一インスタンスを共有して呼び出されるため、内部でロックを用いて保護する。
/// - 外れ値除去（トリム平均・IQRなど）は行わず、単純な平均と母標準偏差のみを算出する。
/// </summary>
internal sealed class HitErrorStatsAccumulator
{
    internal readonly record struct Result(
        double RawAvg,
        double ModifiedAvg,
        double ModifiedStdev,
        double RawUR,
        double ModifiedUR);

    private static readonly Result Empty = new(0, 0, 0, 0, 0);

    private readonly object _lock = new();

    private int _count;
    private double _mean;
    private double _m2; // Welfordのアルゴリズムにおける「平均からの偏差二乗和」
    private int _lastValue;

    /// <summary>
    /// 現在のHitErrorsスナップショットとの差分だけを取り込み、速度Mod補正込みの統計量を返す。
    /// Slow Lane・Fast Laneの双方から、同一インスタンスに対して呼び出すことを想定している。
    /// </summary>
    /// <param name="hitErrors">
    /// 現時点でのHitErrors全体のスナップショット。同じ系譜のリストが末尾に追記されて
    /// 単調に伸びていく前提（osu!側の実際の挙動）。
    /// </param>
    /// <param name="speedMultiplier">DT/HT等による再生速度倍率。offset・UR双方に掛かる。</param>
    internal Result Sync(IReadOnlyList<int> hitErrors, double speedMultiplier)
    {
        lock (_lock)
        {
            int newCount = hitErrors.Count;

            // 巻き戻り（リトライ等でosu!側のHitErrorsがクリアされた）を検知した場合や、
            // 前回取り込んだ末尾要素と食い違う場合（Slow Lane / Fast Laneが別タイミングで
            // 取得した、系譜の異なるスナップショットを取り込もうとした場合）は、
            // 差分だけの追記が安全でないためゼロから作り直す。
            // 通常時（同一系譜のリストが単調に伸びていくだけ）はこの分岐には入らず、
            // 増えた分だけを取り込むO(増分件数)の経路を通る。
            bool discontinuous = newCount < _count
                || (_count > 0 && hitErrors[_count - 1] != _lastValue);

            if (discontinuous)
                Reset();

            for (int i = _count; i < newCount; i++)
                Add(hitErrors[i]);

            if (_count == 0)
                return Empty;

            double rawVariance = _m2 / _count; // 既存実装(TimingHelper)と同じ母分散の定義
            double rawStdev = Math.Sqrt(rawVariance);

            double rawAvg = _mean;
            double modifiedAvg = rawAvg * speedMultiplier;
            double modifiedStdev = rawStdev * speedMultiplier;

            double rawUR = rawStdev * 10;
            double modifiedUR = rawUR * speedMultiplier;

            // 既存実装と同じ異常値ガード（外れ値除去ではなく、破綻値のフォールバックのみ）
            if (rawUR > 10000) rawUR = double.NaN;
            if (modifiedUR > 10000) modifiedUR = double.NaN;

            return new Result(rawAvg, modifiedAvg, modifiedStdev, rawUR, modifiedUR);
        }
    }

    private void Add(int value)
    {
        _count++;
        double delta = value - _mean;
        _mean += delta / _count;
        double delta2 = value - _mean;
        _m2 += delta * delta2;
        _lastValue = value;
    }

    private void Reset()
    {
        _count = 0;
        _mean = 0;
        _m2 = 0;
        _lastValue = 0;
    }
}
