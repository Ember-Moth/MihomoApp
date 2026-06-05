using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aureline.ViewModels;
using Aureline.Views;

namespace Aureline;

public partial class App : Application
{
    public static MainViewModel? CurrentMainViewModel { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = CreateMainView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static bool TryHandleBack()
    {
        return CurrentMainViewModel?.HandleBack() == true;
    }

    private static MainView CreateMainView()
    {
        var viewModel = new MainViewModel();
        CurrentMainViewModel = viewModel;
        return new MainView { DataContext = viewModel };
    }
}
