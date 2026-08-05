using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OsuMate.Models
{
    // 元々 ViewModels 層にあったが、Models/PlayLogEntry.cs や Models/OverlayItem.cs 等の
    // Model 側からも INotifyPropertyChanged 実装として使いたいため、
    // 依存方向が逆転しないよう中立な Models 層に置く。
    public abstract class ObservableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
