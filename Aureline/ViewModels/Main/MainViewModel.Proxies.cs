using CommunityToolkit.Mvvm.Input;

namespace Aureline.ViewModels;

public partial class MainViewModel
{
    private const string DefaultDelayTestUrl = "https://www.gstatic.com/generate_204";

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
        IsProxyGroupListVisible = false;
        MarkSelectedGroup();
    }

    [RelayCommand]
    private void ToggleProxySearch()
    {
        IsProxySearchVisible = !IsProxySearchVisible;
        if (!IsProxySearchVisible)
        {
            ProxySearchText = string.Empty;
        }
    }

    [RelayCommand]
    private void ClearProxySearch()
    {
        ProxySearchText = string.Empty;
        IsProxySearchVisible = false;
    }

    [RelayCommand]
    private void ToggleProxyGroupList()
    {
        IsProxyGroupListVisible = !IsProxyGroupListVisible;
    }

    [RelayCommand]
    private void HideProxyGroupList()
    {
        IsProxyGroupListVisible = false;
    }

    [RelayCommand]
    private void SelectPreviousProxyGroup()
    {
        SelectProxyGroupByOffset(-1);
    }

    [RelayCommand]
    private void SelectNextProxyGroup()
    {
        SelectProxyGroupByOffset(1);
    }

    [RelayCommand]
    private void ShowCurrentProxy()
    {
        if (SelectedGroup == null || string.IsNullOrWhiteSpace(SelectedGroup.Now))
        {
            LastMessage = "当前策略组没有选中节点";
            return;
        }

        ProxySearchText = SelectedGroup.Now;
        IsProxySearchVisible = true;
        LastMessage = $"当前节点: {SelectedGroup.Now}";
    }

    [RelayCommand]
    private void SetProxySortMode(string? sortMode)
    {
        ProxySortMode = ProxySortOptions.Contains(sortMode)
            ? sortMode!
            : ProxySortOptions[0];
        LastMessage = $"代理排序: {ProxySortMode}";
    }

    private void SelectProxyGroupByOffset(int offset)
    {
        if (ProxyGroups.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedGroup == null ? -1 : ProxyGroups.IndexOf(SelectedGroup);
        if (currentIndex < 0)
        {
            SelectedGroup = ProxyGroups[0];
            MarkSelectedGroup();
            return;
        }

        var nextIndex = Math.Clamp(currentIndex + offset, 0, ProxyGroups.Count - 1);
        if (nextIndex == currentIndex)
        {
            return;
        }

        SelectedGroup = ProxyGroups[nextIndex];
        MarkSelectedGroup();
        LastMessage = $"策略组: {SelectedGroup.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanRefreshProxies))]
    private async Task RefreshSelectedGroupAsync()
    {
        await RunAsync(async () =>
        {
            await _runtime.HealthCheckAsync(SelectedGroup?.Name ?? string.Empty);
            await RefreshProxiesCoreAsync();
            LastMessage = SelectedGroup == null ? "策略组已刷新" : $"{SelectedGroup.Name} 已刷新";
        });
    }

    [RelayCommand]
    private async Task SelectProxyAsync(ProxyNodeItem? proxy)
    {
        if (SelectedGroup == null || proxy == null || !IsRunning || !SelectedGroup.IsSelectable)
        {
            LastMessage = SelectedGroup?.IsSelectable == false ? "当前策略组不支持手动切换" : LastMessage;
            return;
        }

        await RunAsync(async () =>
        {
            var selected = await _runtime.SelectProxyAsync(SelectedGroup.Name, proxy.Name);
            if (!selected)
            {
                LastMessage = $"切换失败: {SelectedGroup.Name} -> {proxy.Name}";
                return;
            }

            SelectedGroup.Now = proxy.Name;
            foreach (var node in SelectedGroup.Nodes)
            {
                node.IsSelected = string.Equals(node.Name, proxy.Name, StringComparison.Ordinal);
            }

            if (CloseConnections)
            {
                await _runtime.CloseAllConnectionsAsync();
            }

            LastMessage = $"{SelectedGroup.Name} -> {proxy.Name}";
            OnSelectedGroupChanged(SelectedGroup);
            await RefreshProxiesCoreAsync();
            await RefreshNetworkDetectionAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanTestSelectedGroupDelay))]
    private async Task TestSelectedGroupDelayAsync()
    {
        if (SelectedGroup == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            IsTestingGroupDelay = true;
            try
            {
                var nodes = VisibleProxyNodes.Count > 0
                    ? VisibleProxyNodes.ToArray()
                    : SelectedGroup.Nodes.ToArray();
                await TestDelayBatchAsync(nodes, DelayTestUrl(SelectedGroup), CancellationToken.None);
                UpdateVisibleProxyNodes();
                LastMessage = $"{SelectedGroup.Name} 延迟测试完成";
            }
            finally
            {
                IsTestingGroupDelay = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanTestProxyDelay))]
    private async Task TestProxyDelayAsync(ProxyNodeItem? proxy)
    {
        if (SelectedGroup == null || proxy == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await TestProxyDelayCoreAsync(proxy, DelayTestUrl(SelectedGroup), CancellationToken.None);
            UpdateVisibleProxyNodes();
            LastMessage = $"{proxy.Name} 延迟 {proxy.DelayText}";
        });
    }

    private async Task RefreshProxiesCoreAsync()
    {
        var selectedGroupName = SelectedGroup?.Name;
        var groups = await _runtime.GetProxyGroupsAsync(ProxySortMode);

        ProxyGroups.Clear();

        foreach (var group in groups)
        {
            var item = new ProxyGroupItem(group.Name, group.Type, group.TestUrl)
            {
                Now = group.Now
            };

            foreach (var proxy in group.Proxies)
            {
                item.Nodes.Add(new ProxyNodeItem(proxy.Name, proxy.Type, proxy.Now, proxy.Delay)
                {
                    IsSelected = string.Equals(proxy.Name, group.Now, StringComparison.Ordinal)
                });
            }

            ProxyGroups.Add(item);
        }

        SelectedGroup = ProxyGroups.FirstOrDefault(group => group.Name == selectedGroupName)
            ?? ProxyGroups.FirstOrDefault();
        MarkSelectedGroup();
        UpdateVisibleProxyNodes();
        OnPropertyChanged(nameof(ProxySummary));
        OnPropertyChanged(nameof(HasProxyGroups));
        LastMessage = ProxyGroups.Count == 0 ? "未发现策略组" : "策略组已刷新";
    }

    private async Task TestDelayBatchAsync(
        IReadOnlyList<ProxyNodeItem> nodes,
        string testUrl,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(16);
        var tasks = nodes.Select(async node =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await TestProxyDelayCoreAsync(node, testUrl, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task TestProxyDelayCoreAsync(
        ProxyNodeItem proxy,
        string testUrl,
        CancellationToken cancellationToken)
    {
        proxy.IsTesting = true;
        try
        {
            proxy.Delay =
                await _runtime.TestProxyDelayAsync(
                proxy.Name,
                testUrl,
                cancellationToken: cancellationToken) ??
                -1;
        }
        catch
        {
            proxy.Delay = -1;
        }
        finally
        {
            proxy.IsTesting = false;
        }
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
            UpdateRunningDuration();
            var traffic = await _runtime.GetTrafficAsync();
            var connectionCount = await _runtime.GetConnectionCountAsync();
            if (traffic == null || connectionCount == null)
            {
                return;
            }

            UploadSpeed = $"{FormatBytes(traffic.Up)}/s";
            DownloadSpeed = $"{FormatBytes(traffic.Down)}/s";
            AddSpeedSamples(traffic.Up, traffic.Down);
            TotalUpload = FormatBytes(traffic.UpTotal);
            TotalDownload = FormatBytes(traffic.DownTotal);
            TotalTraffic = FormatBytes(traffic.UpTotal + traffic.DownTotal);
            ConnectionCount = connectionCount.Value.ToString();
        }
        catch
        {
            // Native telemetry may not be ready during startup or may already be stopping.
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

    private void UpdateVisibleProxyNodes()
    {
        VisibleProxyNodes.Clear();
        if (SelectedGroup == null)
        {
            OnPropertyChanged(nameof(VisibleProxySummary));
            OnPropertyChanged(nameof(HasVisibleProxyNodes));
            OnPropertyChanged(nameof(MissingVisibleProxyNodes));
            return;
        }

        IEnumerable<ProxyNodeItem> nodes = SelectedGroup.Nodes;
        if (!string.IsNullOrWhiteSpace(ProxySearchText))
        {
            var query = ProxySearchText.Trim();
            nodes = nodes.Where(node =>
                node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                node.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                node.Now.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        nodes = ProxySortMode switch
        {
            "按延迟" => nodes
                .OrderBy(DelaySortKey)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase),
            "按名称" => nodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase),
            _ => nodes
        };

        foreach (var node in nodes)
        {
            VisibleProxyNodes.Add(node);
        }

        OnPropertyChanged(nameof(VisibleProxySummary));
        OnPropertyChanged(nameof(HasVisibleProxyNodes));
        OnPropertyChanged(nameof(MissingVisibleProxyNodes));
    }

    private bool CanTestSelectedGroupDelay()
    {
        return !IsBusy && IsRunning && SelectedGroup != null && SelectedGroup.Nodes.Count > 0;
    }

    private bool CanTestProxyDelay(ProxyNodeItem? proxy)
    {
        return !IsBusy && IsRunning && SelectedGroup != null && proxy != null;
    }

    private static int DelaySortKey(ProxyNodeItem node)
    {
        return node.Delay switch
        {
            > 0 => node.Delay.Value,
            < 0 => int.MaxValue - 1,
            _ => int.MaxValue
        };
    }

    private static string DelayTestUrl(ProxyGroupItem group)
    {
        return string.IsNullOrWhiteSpace(group.TestUrl) ? DefaultDelayTestUrl : group.TestUrl;
    }

    private void UpdateRunningDuration()
    {
        if (!IsRunning || _startedAt == null)
        {
            RunningDuration = "00:00:00";
            return;
        }

        var elapsed = DateTimeOffset.Now - _startedAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        RunningDuration = elapsed.TotalDays >= 1
            ? $"{(int)elapsed.TotalDays}天 {elapsed:hh\\:mm\\:ss}"
            : elapsed.ToString("hh\\:mm\\:ss");
    }

    private void AddSpeedSamples(long upload, long download)
    {
        AddSample(_uploadSpeedSampleBuffer, upload);
        AddSample(_downloadSpeedSampleBuffer, download);
        UploadSpeedSamples = _uploadSpeedSampleBuffer.ToArray();
        DownloadSpeedSamples = _downloadSpeedSampleBuffer.ToArray();
    }

    private void ResetSpeedSamples()
    {
        _uploadSpeedSampleBuffer.Clear();
        _downloadSpeedSampleBuffer.Clear();
        for (var i = 0; i < 30; i++)
        {
            _uploadSpeedSampleBuffer.Enqueue(0);
            _downloadSpeedSampleBuffer.Enqueue(0);
        }

        UploadSpeedSamples = _uploadSpeedSampleBuffer.ToArray();
        DownloadSpeedSamples = _downloadSpeedSampleBuffer.ToArray();
    }

    private static void AddSample(Queue<double> samples, double value)
    {
        samples.Enqueue(Math.Max(0, value));
        while (samples.Count > 30)
        {
            samples.Dequeue();
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
