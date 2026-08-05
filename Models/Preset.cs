using System;

namespace OsuMate.Models
{
    /// <summary>
    /// 名前付きの設定プリセット1件。
    /// Name はUIから直接リネームされるため、ComboBoxの表示（選択中テキスト／ドロップダウン内リスト双方）を
    /// 自動更新できるよう INotifyPropertyChanged を実装する。
    /// </summary>
    public class Preset : ObservableBase
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "Default";
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public PresetConfig Config { get; set; } = new();
    }
}
