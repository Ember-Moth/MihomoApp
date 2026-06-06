using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Aureline.ViewModels;

namespace Aureline.Views.Pages;

public partial class ConfigPageView : UserControl
{
    public ConfigPageView()
    {
        InitializeComponent();
    }

    private async void ImportProfileFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "文件",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("YAML")
                    {
                        Patterns = ["*.yaml", "*.yml"]
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
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await viewModel.ImportProfileFileCommand.ExecuteAsync(new PickedProfileFile(file.Name, content));
    }

    private async void CopyFocusedProfilePathClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null ||
            DataContext is not MainViewModel viewModel ||
            viewModel.FocusedConfigProfile == null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(viewModel.FocusedConfigProfile.FilePath);
        viewModel.LastMessage = "配置路径已复制";
    }

    private async void CopyFocusedProfileLinkClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null ||
            DataContext is not MainViewModel viewModel ||
            viewModel.FocusedConfigProfile?.IsUrl != true)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(viewModel.FocusedConfigProfile.Url);
        viewModel.LastMessage = "订阅链接已复制";
    }
}
