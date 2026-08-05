using System;
using System.Collections.ObjectModel;
using System.Linq;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// Log タブで表示するカラムの定義・順序・有効/無効を管理する。
    /// SettingsViewModel の他の設定項目（Overlay項目・ウィンドウ位置など）とは状態を共有していないため、
    /// 独立クラスへ切り出した（挙動は切り出し前と同一）。
    /// 実際の永続化（保存）は呼び出し元の SettingsViewModel.Save() に委譲する
    /// （InGameOverlayPriority と LogColumnPriority を同時に書き出す既存の Save() 仕様を維持するため）。
    /// </summary>
    public class LogColumnSettings
    {
        private static readonly (int id, string label)[] LogColumnDefinitions =
        [
            (1,  "Date/Time"),
            (2,  "Artist"),
            (3,  "Title"),
            (4,  "Difficulty"),
            (5,  "Creator"),
            (6,  "BID"),
            (7,  "BSID"),
            (8,  "SR"),
            (9,  "OD"),
            (10, "pp"),
            (11, "Mods"),
            (12, "Acc"),
            (13, "Hits"),
            (14, "Combo"),
            (15, "Player"),
            (16, "Status"),
        ];

        public ObservableCollection<LogColumnItem> LogColumnItems { get; } = [];

        private readonly GlobalConfig _config;
        private readonly Action _save;

        /// <summary>
        /// LogColumnItems はUIスレッド（MoveLogColumnItem/ToggleLogColumnItemによる操作）と
        /// バックグラウンドスレッド（SettingsViewModel.Save() 経由、Timerコールバックから呼ばれる
        /// ToLogColumnPriorityString での列挙）の双方からアクセスされるため、排他制御が必要。
        /// SettingsViewModel.Items とは別のコレクションなので、ロックオブジェクトも分ける
        /// （一方の処理がもう一方をブロックしないようにするため）。
        /// </summary>
        private readonly object _logColumnLock = new();

        public LogColumnSettings(GlobalConfig config, Action save)
        {
            _config = config;
            _save = save;
            LoadLogColumnItems();
        }

        public void MoveLogColumnItem(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            if (fromIndex < 0 || fromIndex >= LogColumnItems.Count) return;
            if (toIndex < 0 || toIndex >= LogColumnItems.Count) return;
            lock (_logColumnLock)
            {
                var item = LogColumnItems[fromIndex];
                LogColumnItems.RemoveAt(fromIndex);
                LogColumnItems.Insert(toIndex, item);
            }
            _save();
        }

        public void ToggleLogColumnItem(LogColumnItem item)
        {
            lock (_logColumnLock) { item.IsEnabled = !item.IsEnabled; }
            _save();
        }

        public string ToLogColumnPriorityString()
        {
            lock (_logColumnLock)
                return string.Join("/", LogColumnItems.Select(i => i.IsEnabled ? i.Id.ToString() : $"-{i.Id}"));
        }

        private void LoadLogColumnItems()
        {
            try
            {
                var priority = _config.LogColumnPriority ?? "1/2/3/4/5/6/7/8/9/10/11/12/13/14/15/16";
                var tokens = priority.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // 順序付きで有効/無効を解析（"-id" = 無効、"id" = 有効）
                var ordered = tokens
                    .Select(t =>
                    {
                        bool enabled = !t.StartsWith('-');
                        string raw = enabled ? t : t[1..];
                        if (int.TryParse(raw, out int id))
                            return (Id: id, Enabled: enabled);
                        return (Id: -1, Enabled: false);
                    })
                    .Where(x => x.Id > 0)
                    .ToList();

                var orderedIds = ordered.Select(x => x.Id).ToList();

                lock (_logColumnLock)
                {
                    LogColumnItems.Clear();
                    foreach (var col in ordered)
                    {
                        var def = LogColumnDefinitions.FirstOrDefault(d => d.id == col.Id);
                        if (def == default) continue;
                        LogColumnItems.Add(new LogColumnItem { Id = col.Id, Label = def.label, IsEnabled = col.Enabled });
                    }
                    // 未登録カラムを末尾に追加（有効状態で）
                    foreach (var def in LogColumnDefinitions)
                    {
                        if (orderedIds.Contains(def.id)) continue;
                        LogColumnItems.Add(new LogColumnItem { Id = def.id, Label = def.label, IsEnabled = true });
                    }
                }
            }
            catch (Exception e)
            {
                LogUtils.DebugLogger("LogColumnSettings.LoadLogColumnItems failed: " + e.Message, true);
                LoadDefaultLogColumnItems();
            }
        }

        private void LoadDefaultLogColumnItems()
        {
            lock (_logColumnLock)
            {
                LogColumnItems.Clear();
                foreach (var def in LogColumnDefinitions)
                    LogColumnItems.Add(new LogColumnItem { Id = def.id, Label = def.label, IsEnabled = true });
            }
        }
    }
}
