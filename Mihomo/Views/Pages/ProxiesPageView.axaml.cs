using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Mihomo.ViewModels;

namespace Mihomo.Views.Pages;

public partial class ProxiesPageView : UserControl
{
    private const double SwipeStartMinY = 96;
    private const double SwipeMinDeltaX = 90;
    private const double SwipeMaxDeltaY = 120;

    private Avalonia.Point? swipeStart;
    private bool isAnimatingSwipe;
    private readonly TranslateTransform proxyPageTransform = new();

    public ProxiesPageView()
    {
        InitializeComponent();
        ProxyPageHost.RenderTransform = proxyPageTransform;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        swipeStart = point.Y >= SwipeStartMinY ? point : null;
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (swipeStart == null || DataContext is not MainViewModel viewModel)
        {
            swipeStart = null;
            return;
        }

        var start = swipeStart.Value;
        swipeStart = null;

        var end = e.GetPosition(this);
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        if (Math.Abs(deltaX) < SwipeMinDeltaX || Math.Abs(deltaY) > SwipeMaxDeltaY)
        {
            return;
        }

        if (deltaX < 0)
        {
            await SwitchProxyGroupAsync(viewModel, 1);
        }
        else
        {
            await SwitchProxyGroupAsync(viewModel, -1);
        }
    }

    private async Task SwitchProxyGroupAsync(MainViewModel viewModel, int direction)
    {
        if (isAnimatingSwipe || !CanSelectProxyGroup(viewModel, direction))
        {
            return;
        }

        if (!viewModel.AnimateTabs)
        {
            ExecuteProxyGroupSwitch(viewModel, direction);
            return;
        }

        isAnimatingSwipe = true;
        var width = Math.Max(Bounds.Width, 320);
        var distance = width * 0.34;

        try
        {
            await AnimateProxyPageAsync(0, -direction * distance, 1, 0.72, 95);
            ExecuteProxyGroupSwitch(viewModel, direction);
            proxyPageTransform.X = direction * distance;
            ProxyPageHost.Opacity = 0.72;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await AnimateProxyPageAsync(direction * distance, 0, 0.72, 1, 130);
        }
        finally
        {
            proxyPageTransform.X = 0;
            ProxyPageHost.Opacity = 1;
            isAnimatingSwipe = false;
        }
    }

    private static bool CanSelectProxyGroup(MainViewModel viewModel, int direction)
    {
        if (viewModel.ProxyGroups.Count == 0)
        {
            return false;
        }

        var currentIndex = viewModel.SelectedGroup == null
            ? -1
            : viewModel.ProxyGroups.IndexOf(viewModel.SelectedGroup);
        if (currentIndex < 0)
        {
            return true;
        }

        var nextIndex = Math.Clamp(currentIndex + direction, 0, viewModel.ProxyGroups.Count - 1);
        return nextIndex != currentIndex;
    }

    private static void ExecuteProxyGroupSwitch(MainViewModel viewModel, int direction)
    {
        if (direction > 0)
        {
            viewModel.SelectNextProxyGroupCommand.Execute(null);
            return;
        }

        viewModel.SelectPreviousProxyGroupCommand.Execute(null);
    }

    private async Task AnimateProxyPageAsync(
        double fromX,
        double toX,
        double fromOpacity,
        double toOpacity,
        int durationMs)
    {
        const int frames = 9;
        for (var frame = 0; frame <= frames; frame++)
        {
            var progress = EaseOutCubic((double)frame / frames);
            proxyPageTransform.X = fromX + (toX - fromX) * progress;
            ProxyPageHost.Opacity = fromOpacity + (toOpacity - fromOpacity) * progress;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (frame < frames)
            {
                await Task.Delay(durationMs / frames);
            }
        }
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1 - value;
        return 1 - inverse * inverse * inverse;
    }
}
