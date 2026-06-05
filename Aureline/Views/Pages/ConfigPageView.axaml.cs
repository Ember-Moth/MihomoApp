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

    private async void CopyProfileLinkClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null ||
            DataContext is not MainViewModel viewModel ||
            (sender as Control)?.DataContext is not ConfigProfileItem profile ||
            !profile.IsUrl)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(profile.Url);
        viewModel.LastMessage = "订阅链接已复制";
    }

    private async void ExportProfileFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null ||
            DataContext is not MainViewModel viewModel ||
            (sender as Control)?.DataContext is not ConfigProfileItem profile)
        {
            return;
        }

        if (!File.Exists(profile.FilePath))
        {
            viewModel.LastMessage = $"配置文件不存在: {profile.FilePath}";
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "导出文件",
                SuggestedFileName = $"{SanitizeFileName(profile.Label)}.yaml",
                FileTypeChoices =
                [
                    new FilePickerFileType("YAML")
                    {
                        Patterns = ["*.yaml", "*.yml"]
                    }
                ]
            });
        if (file == null)
        {
            return;
        }

        await using var output = await file.OpenWriteAsync();
        await using var input = File.OpenRead(profile.FilePath);
        await input.CopyToAsync(output);
        viewModel.LastMessage = "配置文件已导出";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(c => invalidChars.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "config" : result;
    }
}
