using OsuMate.Models;
using OsuMate.Utils;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace OsuMate.Services
{
    /// <summary>
    /// プリセット（<see cref="Preset"/>）の作成・複製・リネーム・削除・切り替え・保存を一元管理する。
    /// </summary>
    public class PresetManager
    {
        private readonly RootConfig _root;

        /// <summary>設定済みのプリセット一覧（UIへそのままバインドされる）。</summary>
        public ObservableCollection<Preset> Presets { get; } = [];

        /// <summary>現在アクティブなプリセットのID。</summary>
        public string ActivePresetId { get; private set; }

        /// <summary>
        /// 現在アクティブなプリセットの設定インスタンス。
        /// SettingsViewModel側の各プロパティが直接フィールドを書き込む共有インスタンスであり、
        /// プリセット切り替え時（<see cref="ActivePresetChanged"/>発火時）に参照先が入れ替わる。
        /// </summary>
        public PresetConfig ActiveConfig { get; private set; }

        /// <summary>
        /// プリセットが切り替わった（明示的な切り替えに加え、作成・複製・削除に伴う自動切り替えも含む）ときに発火する。
        /// 呼び出し元は新しい <see cref="ActiveConfig"/> を使って画面表示を更新する必要がある。
        /// </summary>
        public event Action<PresetConfig>? ActivePresetChanged;

        public PresetManager(RootConfig root)
        {
            _root = root;

            var active = _root.Presets.FirstOrDefault(p => p.Id == _root.ActivePresetId) ?? _root.Presets[0];
            ActivePresetId = active.Id;
            ActiveConfig = active.Config;

            foreach (var preset in _root.Presets) Presets.Add(preset);
        }

        /// <summary>
        /// プリセット選択ComboBoxの SelectedItem に直接バインドする（オブジェクト参照ベース）。
        /// SelectedValue＋SelectedValuePath による文字列ID照合は、ItemsSource の反映タイミング次第で
        /// 初期選択が解決されず表示が空欄になることがあるため、より確実な SelectedItem 方式に統一する。
        /// </summary>
        public Preset? SelectedPreset
        {
            get => Presets.FirstOrDefault(p => p.Id == ActivePresetId);
            set
            {
                if (value == null || value.Id == ActivePresetId) return;
                SwitchToPreset(value.Id);
            }
        }

        public void SwitchToPreset(string id)
        {
            var preset = Presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return;

            ActivePresetId = id;
            _root.ActivePresetId = id;
            ActiveConfig = preset.Config;

            ActivePresetChanged?.Invoke(ActiveConfig);
            Save();
        }

        /// <summary>現在の設定内容で新規プリセットを作成する（Overlay等は初期値になる）。作成後は自動的に切り替える。</summary>
        public Preset CreatePreset(string name)
        {
            var preset = new Preset
            {
                Name = string.IsNullOrWhiteSpace(name) ? "New Preset" : name.Trim(),
                Config = new PresetConfig(),
            };
            _root.Presets.Add(preset);
            Presets.Add(preset);
            SwitchToPreset(preset.Id);
            return preset;
        }

        /// <summary>指定したプリセットの内容を複製した新規プリセットを作成する。作成後は自動的に切り替える。</summary>
        public Preset DuplicatePreset(string sourceId, string newName)
        {
            var source = Presets.FirstOrDefault(p => p.Id == sourceId) ?? Presets.First();
            var preset = new Preset
            {
                Name = string.IsNullOrWhiteSpace(newName) ? source.Name + " - Copy" : newName.Trim(),
                Config = CloneConfig(source.Config),
            };
            _root.Presets.Add(preset);
            Presets.Add(preset);
            SwitchToPreset(preset.Id);
            return preset;
        }

        public void RenamePreset(string id, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            var preset = Presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return;

            // Preset が INotifyPropertyChanged を実装しているため、
            // これだけで ComboBox の選択中表示・ドロップダウン内リストの両方が自動的に更新される。
            preset.Name = newName.Trim();
            Save();
        }

        /// <summary>プリセットを削除する。最後の1件は削除できない。削除対象が現在選択中の場合は先頭に切り替える。</summary>
        public bool DeletePreset(string id)
        {
            if (Presets.Count <= 1) return false;
            var preset = Presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return false;

            bool wasActive = ActivePresetId == id;
            _root.Presets.Remove(preset);
            Presets.Remove(preset);

            if (wasActive)
                SwitchToPreset(Presets[0].Id);
            else
                Save();

            return true;
        }

        private static PresetConfig CloneConfig(PresetConfig source)
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<PresetConfig>(json) ?? new PresetConfig();
        }

        /// <summary>アクティブプリセットの中身を反映してから RootConfig 全体を保存する。</summary>
        public void Save()
        {
            var active = _root.Presets.FirstOrDefault(p => p.Id == ActivePresetId);
            if (active != null) active.Config = ActiveConfig;
            ConfigUtils.SaveRootConfig(_root);
        }
    }
}
