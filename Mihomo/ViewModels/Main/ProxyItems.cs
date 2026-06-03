using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mihomo.ViewModels;

public sealed partial class ProxyGroupItem : ObservableObject
{
    public ProxyGroupItem(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }

    public string Type { get; }

    public ObservableCollection<ProxyNodeItem> Nodes { get; } = [];

    [ObservableProperty]
    private string _now = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public string Summary => string.IsNullOrWhiteSpace(Now)
        ? $"{Type} · {Nodes.Count} nodes"
        : $"{Type} · {Now}";

    partial void OnNowChanged(string value)
    {
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed partial class ProxyNodeItem : ObservableObject
{
    public ProxyNodeItem(string name, string type, int? delay)
    {
        Name = name;
        Type = string.IsNullOrWhiteSpace(type) ? "Proxy" : type;
        Delay = delay;
    }

    public string Name { get; }

    public string Type { get; }

    public int? Delay { get; }

    public string DelayText => Delay is > 0 ? $"{Delay} ms" : "-";

    [ObservableProperty]
    private bool _isSelected;
}
