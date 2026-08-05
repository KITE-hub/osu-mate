using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OsuMate.Models
{
    /// <summary>
    /// プリセットとは独立した共通設定。
    /// Overall セクション・Log Columns・PC環境依存の設定を含む。
    /// </summary>
    public class GlobalConfig
    {
        public string FontFamily { get; set; } = "Oxanium";
        public bool IsDarkMode { get; set; } = true;

        /// <summary>Log絞り込み・Best pp算出の対象プレイヤー名一覧。大文字小文字は区別しない。</summary>
        public List<string> TargetPlayerNames { get; set; } = new();

        /// <summary>Log タブのカラム順・表示設定。InGameOverlayPriority と同形式のスラッシュ区切り文字列。</summary>
        public string LogColumnPriority { get; set; } = "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15";

        /// <summary>
        /// true（既定値）= Logタブに中断（リザルト画面まで完了しなかった）プレイも表示する。
        /// false = 中断プレイを一覧から除外し、完了したプレイのみ表示する。
        /// </summary>
        public bool ShowAbortedPlays { get; set; } = true;

        /// <summary>プレイ履歴 JSON の出力フォルダ。空文字の場合は実行ファイルと同じ場所の PlayLogs/ を使用。</summary>
        public string LogOutputDir { get; set; } = "";

        /// <summary>
        /// osu!.exe が格納されているフォルダ（プレイ履歴JSONの取り込み・SR/pp計算・osu!.dbの参照に使用）。
        /// 空文字の場合はosu!プロセスから自動取得する。
        /// 「起動時に自動起動したいosu!」（<see cref="AutoLaunchOsuPath"/>）とは別
        /// </summary>
        public string OsuExeDirectory { get; set; } = "";

        /// <summary>
        /// 後方互換専用フィールド。旧バージョン（<see cref="OsuExeDirectory"/> への改称前）の
        /// Config.json に残る "OsuDirectory" キーを読み込むためだけに存在する。
        /// <see cref="OsuMate.Utils.ConfigUtils"/> の読み込み時に一度だけ <see cref="OsuExeDirectory"/> へ移行され、
        /// 以後（保存後）はConfig.json上から消える。アプリの他の場所からは参照しないこと。
        /// </summary>
        [JsonPropertyName("OsuDirectory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyOsuDirectory { get; set; }

        /// <summary>
        /// osu mate起動時に自動的に起動したい osu! の実行ファイル（.exe）またはショートカット（.lnk）への
        /// フルパス。空文字の場合は自動起動しない。
        /// ショートカットを指定できるようにしているのは、公式のbanchoサーバーではなく
        /// プライベートサーバー接続用の起動ショートカットを自動起動したいケースを想定しているため。
        /// </summary>
        public string AutoLaunchOsuPath { get; set; } = "";

        /// <summary>
        /// <see cref="AutoLaunchOsuPath"/> による自動起動機能そのものの有効/無効。
        /// true（既定値）= パスが設定されていれば起動時に自動起動する。
        /// false = パスが設定されていても自動起動しない（パス自体は保持したまま一時的にオフにできる）。
        /// 「有効/無効」と「起動先パス」は別
        public bool AutoLaunchOsuEnabled { get; set; } = true;

        /// <summary>
        /// Trainer画面の "Adjust Pitch with Speed" トグルの状態。
        /// true = 速度変化と一緒にピッチも変化させる（テープ再生風）。
        /// </summary>
        public bool AdjustPitchWithSpeed { get; set; } = false;

        /// <summary>
        /// osu!プロセスからのメモリ読み取り（Fast Lane: <see cref="OsuMate.Services.OsuMemoryService.StartMemoryReader"/>）と
        /// pp/Strain計算（Slow Lane: <see cref="OsuMate.Services.PpCalculationService.Start"/>）が共有する更新間隔（ミリ秒）。
        /// URBarの判定ドット、StrainGraph/URTimeGraph/URDistGraph、pp・スコア表示の更新頻度は
        /// いずれもこの値で律速される。値を大きくするほど各表示の更新は粗くなるが、
        /// osu!本体への負荷（ReadProcessMemory呼び出し頻度）は下がる。
        /// </summary>
        public int DataUpdateIntervalMs { get; set; } = 15;
    }
}
