using CommunityToolkit.Mvvm.Input;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanRefreshProxies))]
    private async Task RefreshProxiesAsync()
    {
        await RunAsync(RefreshProxiesCoreAsync);
    }

    [RelayCommand]
    private void SelectGroup(ProxyGroupItem? group)
    {
        if (group == null)
        {
            return;
        }

        SelectedGroup = group;
        MarkSelectedGroup();
    }

    [RelayCommand]
    private async Task SelectProxyAsync(ProxyNodeItem? proxy)
    {
        if (SelectedGroup == null || proxy == null || !IsRunning)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _api.SelectProxyAsync(SelectedGroup.Name, proxy.Name);
            SelectedGroup.Now = proxy.Name;
            foreach (var node in SelectedGroup.Nodes)
            {
                node.IsSelected = string.Equals(node.Name, proxy.Name, StringComparison.Ordinal);
            }

            LastMessage = $"{SelectedGroup.Name} -> {proxy.Name}";
            await RefreshProxiesCoreAsync();
        });
    }

    private async Task RefreshProxiesCoreAsync()
    {
        var selectedGroupName = SelectedGroup?.Name;
        var groups = await _api.GetProxyGroupsAsync();
        ProxyGroups.Clear();

        foreach (var group in groups)
        {
            var item = new ProxyGroupItem(group.Name, group.Type)
            {
                Now = group.Now
            };

            foreach (var proxy in group.Proxies)
            {
                item.Nodes.Add(new ProxyNodeItem(proxy.Name, proxy.Type, proxy.Delay)
                {
                    IsSelected = string.Equals(proxy.Name, group.Now, StringComparison.Ordinal)
                });
            }

            ProxyGroups.Add(item);
        }

        SelectedGroup = ProxyGroups.FirstOrDefault(group => group.Name == selectedGroupName)
            ?? ProxyGroups.FirstOrDefault();
        MarkSelectedGroup();
        OnPropertyChanged(nameof(ProxySummary));
        LastMessage = ProxyGroups.Count == 0 ? "未发现策略组" : "策略组已刷新";
    }

    private async Task RefreshTelemetryAsync()
    {
        if (!IsRunning || _isRefreshingTelemetry)
        {
            return;
        }

        try
        {
            _isRefreshingTelemetry = true;
            var trafficTask = _api.GetTrafficAsync();
            var connectionsTask = _api.GetConnectionCountAsync();
            await Task.WhenAll(trafficTask, connectionsTask);

            var traffic = trafficTask.Result;
            UploadSpeed = $"{FormatBytes(traffic.Up)}/s";
            DownloadSpeed = $"{FormatBytes(traffic.Down)}/s";
            TotalTraffic = FormatBytes(traffic.UpTotal + traffic.DownTotal);
            ConnectionCount = connectionsTask.Result.ToString();
        }
        catch
        {
            // The controller may not be ready during startup or may already be stopping.
        }
        finally
        {
            _isRefreshingTelemetry = false;
        }
    }

    private void MarkSelectedGroup()
    {
        foreach (var group in ProxyGroups)
        {
            group.IsSelected = ReferenceEquals(group, SelectedGroup);
        }
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var current = Math.Max(0, (double)value);
        var unit = 0;
        while (current >= 1024 && unit < units.Length - 1)
        {
            current /= 1024;
            unit++;
        }

        return unit == 0 ? $"{current:0} {units[unit]}" : $"{current:0.0} {units[unit]}";
    }
}
