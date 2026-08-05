using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OsuMate.Models;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// PlayLogView の分割ファイル: DataGrid の列生成・表示切り替えロジックを担当する。
    /// </summary>
    public partial class PlayLogView
    {
        // IValueConverter はステートレスなので static インスタンスを共有する
        private static readonly NullableDoubleValueConverter   _nullableDoubleConverter = new();
        private static readonly ModsDisplayValueConverter      _modsDisplayConverter    = new();
        private static readonly PlayLogHitsValueConverter      _hitsConverter           = new();
        private static readonly BoolToStatusValueConverter     _boolToStatusConverter   = new();

        // DataGridColumn → カラムID のマッピング（Tag の代わり）
        private readonly Dictionary<DataGridColumn, int> _columnIdMap = new();

        private void BuildAllColumns(PlayLogViewModel vm)
        {
            LogGrid.Columns.Clear();
            _columnIdMap.Clear();

            foreach (var col in vm.AllColumns)
            {
                var gridCol = CreateColumn(col);
                gridCol.Visibility = col.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                _columnIdMap[gridCol] = col.Id;
                LogGrid.Columns.Add(gridCol);
            }
        }

        private void UpdateColumnVisibility(IReadOnlyList<LogColumnItem> activeColumns)
        {
            if (DataContext is not PlayLogViewModel vm) return;

            // 順序が変わった場合は再生成
            var activeIds = activeColumns.Select(c => c.Id).ToList();
            var currentVisible = LogGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible && _columnIdMap.ContainsKey(c))
                .Select(c => _columnIdMap[c])
                .ToList();

            if (!activeIds.SequenceEqual(currentVisible) && LogGrid.Columns.Count > 0)
            {
                // 順序変更の場合のみ再生成
                var allIds = vm.AllColumns.Select(c => c.Id).ToList();
                var currentAllIds = LogGrid.Columns
                    .Where(c => _columnIdMap.ContainsKey(c))
                    .Select(c => _columnIdMap[c])
                    .ToList();
                if (!allIds.SequenceEqual(currentAllIds))
                {
                    BuildAllColumns(vm);
                    return;
                }
            }

            // 順序が同じなら Visibility だけ更新
            var enabledIds = new HashSet<int>(activeColumns.Select(c => c.Id));
            foreach (var col in LogGrid.Columns)
            {
                if (_columnIdMap.TryGetValue(col, out int id))
                    col.Visibility = enabledIds.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private DataGridTextColumn CreateColumn(LogColumnItem col)
        {
            return col.Id switch
            {
                1  => Make("Date/Time",  new Binding("PlayedAt") { StringFormat = "{0:MM/dd HH:mm}" }, 80),
                2  => Make("Artist",     new Binding("Artist"),         85),
                3  => Make("Title",      new Binding("Title"),          110),
                4  => Make("Difficulty", new Binding("DifficultyName"), 85),
                5  => Make("Creator",    new Binding("Creator"),         90),
                6  => Make("BID",        new Binding("BeatmapId"),       60),
                7  => Make("BSID",       new Binding("BeatmapSetId"),    60),
                8  => MakeConverter("SR",     new Binding("StarRating"),  _nullableDoubleConverter, 45,  "F2"),
                9  => Make("OD",  new Binding("OverallDifficulty") { StringFormat = "{0:F1}" }, 40),
                10 => MakeConverter("pp",     new Binding("Pp"),          _nullableDoubleConverter, 55,  "F2"),
                11 => MakeConverter("Mods",   new Binding("ModsString"),  _modsDisplayConverter,    80),
                12 => Make("Acc", new Binding("Accuracy") { StringFormat = "{0:F2}" }, 55),
                13 => MakeConverter("Hits",   new Binding("."),           _hitsConverter,           170),
                14 => Make("Combo", new Binding("MaxCombo") { StringFormat = "{0}x" }, 60),
                15 => Make("Player",    new Binding("PlayerName"),      70),
                16 => MakeConverter("Status", new Binding("IsCompleted"), _boolToStatusConverter,   54),
                _  => Make(col.Label, new Binding("."), 80),
            };
        }

        private static DataGridTextColumn Make(string header, Binding binding, double width)
            => new() { Header = header, Binding = binding, Width = width };

        private static DataGridTextColumn MakeConverter(string header, Binding binding,
            IValueConverter converter, double width, string? param = null)
        {
            binding.Converter = converter;
            if (param != null) binding.ConverterParameter = param;
            return new DataGridTextColumn { Header = header, Binding = binding, Width = width };
        }
    }
}
