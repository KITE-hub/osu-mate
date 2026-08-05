using System.Windows;
using OsuMate.Utils;

namespace OsuMate.Utils;

internal static class FormUtils
{
    internal static void ShowErrorMessageBox(string message) =>
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
}
