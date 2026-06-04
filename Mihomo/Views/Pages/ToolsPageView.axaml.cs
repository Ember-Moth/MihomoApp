using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using Mihomo.ViewModels;

namespace Mihomo.Views.Pages;

public partial class ToolsPageView : UserControl
{
    private INotifyPropertyChanged? observedViewModel;
    private string currentToolPage = string.Empty;
    private int transitionVersion;
    private readonly TranslateTransform toolContentTransform = new();

    public ToolsPageView()
    {
        InitializeComponent();
        ToolContentHost.RenderTransform = toolContentTransform;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (observedViewModel != null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = DataContext as INotifyPropertyChanged;
        currentToolPage = (DataContext as MainViewModel)?.CurrentToolPage ?? string.Empty;

        if (observedViewModel != null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentToolPage) || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var nextPage = viewModel.CurrentToolPage;
        var forward = string.IsNullOrWhiteSpace(currentToolPage) || !string.IsNullOrWhiteSpace(nextPage);
        currentToolPage = nextPage;
        _ = AnimateToolPageTransitionAsync(forward);
    }

    private async Task AnimateToolPageTransitionAsync(bool forward)
    {
        var version = ++transitionVersion;
        var fromX = forward ? 42d : -42d;
        const int frames = 9;

        toolContentTransform.X = fromX;
        ToolContentHost.Opacity = 0.78;

        for (var frame = 0; frame <= frames && version == transitionVersion; frame++)
        {
            var progress = EaseOutCubic((double)frame / frames);
            toolContentTransform.X = fromX * (1 - progress);
            ToolContentHost.Opacity = 0.78 + 0.22 * progress;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (frame < frames)
            {
                await Task.Delay(16);
            }
        }

        if (version != transitionVersion)
        {
            return;
        }

        toolContentTransform.X = 0;
        ToolContentHost.Opacity = 1;
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1 - value;
        return 1 - inverse * inverse * inverse;
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
