namespace OsuMate.Models
{
    /// <summary>
    /// Log タブで表示するカラムの1件。OverlayItem と同パターン。
    /// </summary>
    public class LogColumnItem : ObservableBase
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }
}
