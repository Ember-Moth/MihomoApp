using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mihomo.ViewModels;

namespace Mihomo.Views.Pages;

public partial class ToolsPageView : UserControl
{
    public ToolsPageView()
    {
        InitializeComponent();
    }

    private async void ExportBackupClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "备份",
                SuggestedFileName = $"mihomo-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
                FileTypeChoices =
                [
                    new FilePickerFileType("ZIP")
                    {
                        Patterns = ["*.zip"]
                    }
                ]
            });
        if (file == null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await viewModel.WriteBackupAsync(stream);
    }

    private async void RestoreBackupClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "恢复",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("ZIP")
                    {
                        Patterns = ["*.zip"]
                    },
                    FilePickerFileTypes.All
                ]
            });
        var file = files.FirstOrDefault();
        if (file == null)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync();
        await viewModel.RestoreBackupAsync(stream);
    }
}
