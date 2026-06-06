using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aureline.ViewModels;

public sealed partial class ShellNavigationItem : ObservableObject
{
    public ShellNavigationItem(string pageKey, string label, string icon, ICommand command)
    {
        PageKey = pageKey;
        Label = label;
        Icon = icon;
        Command = command;
    }

    public string PageKey { get; }

    public string Label { get; }

    public string Icon { get; }

    public ICommand Command { get; }

    [ObservableProperty]
    private bool _isActive;
}
