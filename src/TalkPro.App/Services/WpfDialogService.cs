using System.Windows;
using Microsoft.Win32;
using TalkPro.Presentation.Abstractions;

namespace TalkPro.App.Services;

/// <summary>WPF dialogs. The chosen path is returned to the workflow only; it is never logged.</summary>
internal sealed class WpfDialogService : IDialogService
{
    public string? PickExportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "메신저 대화 내보내기 파일 선택",
            Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}
