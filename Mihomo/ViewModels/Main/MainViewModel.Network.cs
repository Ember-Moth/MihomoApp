using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
    private sealed record IpInfo(string Ip, string CountryCode);

    private static readonly IReadOnlyList<(string Url, Func<JsonElement, IpInfo> Parser)> IpInfoSources =
    [
        ("https://ipwho.is", json => ParseIp(json, "ip", "country_code")),
        ("https://api.myip.com", json => ParseIp(json, "ip", "cc")),
        ("https://ipapi.co/json", json => ParseIp(json, "ip", "country_code")),
        ("https://ident.me/json", json => ParseIp(json, "ip", "cc")),
        ("http://ip-api.com/json", json => ParseIp(json, "query", "countryCode")),
        ("https://api.ip.sb/geoip", json => ParseIp(json, "ip", "country_code")),
        ("https://ipinfo.io/json", json => ParseIp(json, "ip", "country")),
    ];

    [RelayCommand]
    private async Task RefreshNetworkDetectionAsync()
    {
        if (_isRefreshingNetworkDetection)
        {
            return;
        }

        try
        {
            _isRefreshingNetworkDetection = true;
            IsNetworkDetectionLoading = true;
            PublicIp = "检测中";
            PublicIpCountryCode = string.Empty;

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var tasks = IpInfoSources
                .Select(source => QueryIpInfoAsync(source.Url, source.Parser, cancellation.Token))
                .ToList();

            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);
                var result = await completed;
                if (result == null)
                {
                    continue;
                }

                cancellation.Cancel();
                ApplyIpInfo(result);
                return;
            }

            ApplyNetworkDetectionTimeout();
        }
        catch
        {
            ApplyNetworkDetectionTimeout();
        }
        finally
        {
            _isRefreshingNetworkDetection = false;
        }
    }

    private async Task<IpInfo?> QueryIpInfoAsync(
        string url,
        Func<JsonElement, IpInfo> parser,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("clash");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return parser(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyIpInfo(IpInfo ipInfo)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyIpInfo(ipInfo));
            return;
        }

        PublicIp = ipInfo.Ip;
        PublicIpCountryCode = ipInfo.CountryCode;
        IsNetworkDetectionLoading = false;
    }

    private void ApplyNetworkDetectionTimeout()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyNetworkDetectionTimeout);
            return;
        }

        PublicIp = "Timeout";
        PublicIpCountryCode = string.Empty;
        IsNetworkDetectionLoading = false;
    }

    private static IpInfo ParseIp(JsonElement json, string ipField, string countryCodeField)
    {
        if (!json.TryGetProperty(ipField, out var ipValue) ||
            !json.TryGetProperty(countryCodeField, out var countryCodeValue))
        {
            throw new FormatException("Invalid IP info response");
        }

        return new IpInfo(
            ipValue.GetString() ?? string.Empty,
            countryCodeValue.GetString() ?? string.Empty);
    }
}
