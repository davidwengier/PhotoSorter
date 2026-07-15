using Microsoft.Win32;

namespace PhotoSorter.App.Services;

public interface IUserDialogService
{
    string? ChooseFolder(string title, string? initialDirectory = null);

    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);
}

public sealed class UserDialogService : IUserDialogService
{
    public string? ChooseFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
