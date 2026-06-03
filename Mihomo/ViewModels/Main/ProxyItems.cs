using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mihomo.ViewModels;

public sealed partial class ProxyGroupItem : ObservableObject
{
    public ProxyGroupItem(string name, string type, string testUrl)
    {
        Name = name;
        Type = string.IsNullOrWhiteSpace(type) ? "Selector" : type;
        TestUrl = testUrl;
    }

    public string Name { get; }

    public string Type { get; }

    public string TestUrl { get; }

    public ObservableCollection<ProxyNodeItem> Nodes { get; } = [];

    [ObservableProperty]
    private string _now = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsComputed => IsComputedGroupType(Type);

    public bool IsSelectable => IsSelectorGroupType(Type) || IsComputed;

    public string TypeText => Type switch
    {
        "Selector" or "select" => "手动选择",
        "URLTest" or "url-test" => "自动测速",
        "Fallback" or "fallback" => "故障转移",
        "LoadBalance" or "load-balance" => "负载均衡",
        "Relay" or "relay" => "链式代理",
        _ => Type
    };

    public string Summary => string.IsNullOrWhiteSpace(Now)
        ? $"{TypeText} · {Nodes.Count} 个节点"
        : $"{TypeText} · {Now}";

    public string CountText => $"{Nodes.Count} 节点";

    partial void OnNowChanged(string value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    private static bool IsSelectorGroupType(string value)
    {
        return value.Equals("selector", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("select", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComputedGroupType(string value)
    {
        return value.Equals("url-test", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("urltest", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fallback", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class ProxyNodeItem : ObservableObject
{
    public ProxyNodeItem(string name, string type, string now, int? delay)
    {
        Name = name;
        Type = string.IsNullOrWhiteSpace(type) ? "Proxy" : type;
        Now = now;
        _delay = delay;
    }

    public string Name { get; }

    public string Type { get; }

    public string Now { get; }

    [ObservableProperty]
    private int? _delay;

    [ObservableProperty]
    private bool _isTesting;

    public string DelayText
    {
        get
        {
            if (IsTesting)
            {
                return "测试中";
            }

            return Delay switch
            {
                > 0 => $"{Delay} ms",
                < 0 => "超时",
                _ => "-"
            };
        }
    }

    public string Description
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Now))
            {
                return TypeText;
            }

            return $"{TypeText} -> {Now}";
        }
    }

    public string TypeText => Type switch
    {
        "Selector" or "select" => "策略组",
        "URLTest" or "url-test" => "自动测速",
        "Fallback" or "fallback" => "故障转移",
        "LoadBalance" or "load-balance" => "负载均衡",
        "Relay" or "relay" => "链式代理",
        "Direct" or "DIRECT" => "直连",
        "Reject" or "REJECT" => "拒绝",
        _ => Type
    };

    [ObservableProperty]
    private bool _isSelected;

    partial void OnDelayChanged(int? value)
    {
        OnPropertyChanged(nameof(DelayText));
    }

    partial void OnIsTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(DelayText));
    }
}
