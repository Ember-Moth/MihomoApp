using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mihomo.ViewModels;

namespace Mihomo.Views.Pages;

public partial class ProxiesPageView : UserControl
{
    private const double SwipeStartMinY = 96;
    private const double SwipeMinDeltaX = 90;
    private const double SwipeMaxDeltaY = 120;

    private Avalonia.Point? swipeStart;

    public ProxiesPageView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        swipeStart = point.Y >= SwipeStartMinY ? point : null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
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
            viewModel.SelectNextProxyGroupCommand.Execute(null);
        }
        else
        {
            viewModel.SelectPreviousProxyGroupCommand.Execute(null);
        }
    }
}
