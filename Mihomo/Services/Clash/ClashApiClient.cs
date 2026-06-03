using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Mihomo.Services.Clash;

public sealed class ClashApiClient
{
    private const int JsonBufferSize = 16 * 1024;

    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private string baseAddress;

    public ClashApiClient(string baseAddress)
    {
        this.baseAddress = NormalizeBaseAddress(baseAddress);
    }

    public void SetBaseAddress(string value)
    {
        baseAddress = NormalizeBaseAddress(value);
    }

    public async Task<IReadOnlyList<ClashApiProxyGroup>> GetProxyGroupsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildUri("/proxies"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            stream,
            ClashApiJsonContext.Default.ClashProxiesResponse,
            cancellationToken);
        if (payload?.Proxies is not { Count: > 0 } proxyDocuments)
        {
            return [];
        }

        var proxies = new Dictionary<string, ProxyDocument>(proxyDocuments.Count, StringComparer.Ordinal);
        foreach (var (name, proxy) in proxyDocuments)
        {
            proxies[name] = ParseProxy(name, proxy);
        }

        var groups = new List<ClashApiProxyGroup>();
        foreach (var proxy in proxies.Values)
        {
            if (proxy.All.Count == 0)
            {
                continue;
            }

            var nodes = new List<ClashApiProxy>(proxy.All.Count);
            foreach (var nodeName in proxy.All)
            {
                if (proxies.TryGetValue(nodeName, out var node))
                {
                    nodes.Add(new ClashApiProxy(node.Name, node.Type, node.Delay));
                }
                else
                {
                    nodes.Add(new ClashApiProxy(nodeName, string.Empty, null));
                }
            }

            groups.Add(new ClashApiProxyGroup(proxy.Name, proxy.Type, proxy.Now, nodes));
        }

        return groups
            .OrderBy(group => group.Name == "GLOBAL" ? 0 : 1)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SelectProxyAsync(
        string groupName,
        string proxyName,
        CancellationToken cancellationToken = default)
    {
        var request = new ClashProxySelectionRequest(proxyName);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, ClashApiJsonContext.Default.ClashProxySelectionRequest),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.PutAsync(
            BuildUri($"/proxies/{Uri.EscapeDataString(groupName)}"),
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<ClashTraffic> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildUri("/traffic"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            stream,
            ClashApiJsonContext.Default.ClashTrafficResponse,
            cancellationToken) ?? new ClashTrafficResponse();

        return new ClashTraffic(
            payload.Up,
            payload.Down,
            payload.UpTotal,
            payload.DownTotal);
    }

    public async Task<int> GetConnectionCountAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildUri("/connections"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await CountConnectionsAsync(stream, cancellationToken);
    }

    private Uri BuildUri(string path)
    {
        return new Uri(baseAddress + path);
    }

    private static string NormalizeBaseAddress(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:9090" : value.Trim();
        return normalized.TrimEnd('/');
    }

    private static ProxyDocument ParseProxy(string fallbackName, ClashProxyDocument document)
    {
        var name = document.Name ?? string.Empty;
        var type = document.Type ?? string.Empty;
        var now = document.Now ?? string.Empty;
        var all = document.All ?? [];
        var delay = ReadDelay(document);

        return new ProxyDocument(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name,
            type,
            now,
            all,
            delay);
    }

    private static int? ReadDelay(ClashProxyDocument document)
    {
        if (document.Delay is { } directDelay)
        {
            return directDelay;
        }

        if (document.History is not { Count: > 0 } history)
        {
            return null;
        }

        int? delay = null;
        foreach (var item in history)
        {
            if (item.Delay is { } itemDelay)
            {
                delay = itemDelay;
            }
        }

        return delay;
    }

    private static async Task<int> CountConnectionsAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(JsonBufferSize);
        var bytesInBuffer = 0;
        var state = new JsonReaderState();
        var pendingConnectionsValue = false;
        var inConnectionsArray = false;
        var connectionsArrayDepth = -1;
        var count = 0;

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(bytesInBuffer, buffer.Length - bytesInBuffer),
                    cancellationToken);
                var isFinalBlock = read == 0;
                var length = bytesInBuffer + read;
                var consumed = CountConnectionsInBlock(
                    buffer.AsSpan(0, length),
                    isFinalBlock,
                    ref state,
                    ref pendingConnectionsValue,
                    ref inConnectionsArray,
                    ref connectionsArrayDepth,
                    ref count,
                    out var completed);

                bytesInBuffer = length - consumed;
                if (bytesInBuffer > 0)
                {
                    buffer.AsSpan(consumed, bytesInBuffer).CopyTo(buffer);
                }

                if (completed)
                {
                    return count;
                }

                if (isFinalBlock)
                {
                    return 0;
                }

                if (bytesInBuffer == buffer.Length)
                {
                    throw new JsonException("JSON token is too large.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int CountConnectionsInBlock(
        ReadOnlySpan<byte> data,
        bool isFinalBlock,
        ref JsonReaderState state,
        ref bool pendingConnectionsValue,
        ref bool inConnectionsArray,
        ref int connectionsArrayDepth,
        ref int count,
        out bool completed)
    {
        var reader = new Utf8JsonReader(data, isFinalBlock, state);
        completed = false;

        while (reader.Read())
        {
            if (inConnectionsArray)
            {
                if (reader.TokenType == JsonTokenType.EndArray &&
                    reader.CurrentDepth == connectionsArrayDepth)
                {
                    completed = true;
                    break;
                }

                if (reader.CurrentDepth == connectionsArrayDepth + 1 &&
                    IsJsonValueToken(reader.TokenType))
                {
                    count++;
                }

                continue;
            }

            if (pendingConnectionsValue)
            {
                pendingConnectionsValue = false;
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    inConnectionsArray = true;
                    connectionsArrayDepth = reader.CurrentDepth;
                }

                continue;
            }

            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.CurrentDepth == 1 &&
                reader.ValueTextEquals("connections"u8))
            {
                pendingConnectionsValue = true;
            }
        }

        state = reader.CurrentState;
        return (int)reader.BytesConsumed;
    }

    private static bool IsJsonValueToken(JsonTokenType tokenType)
    {
        return tokenType is
            JsonTokenType.StartObject or
            JsonTokenType.StartArray or
            JsonTokenType.String or
            JsonTokenType.Number or
            JsonTokenType.True or
            JsonTokenType.False or
            JsonTokenType.Null;
    }

    private sealed record ProxyDocument(
        string Name,
        string Type,
        string Now,
        IReadOnlyList<string> All,
        int? Delay);
}

public sealed record ClashApiProxyGroup(
    string Name,
    string Type,
    string Now,
    IReadOnlyList<ClashApiProxy> Proxies);

public sealed record ClashApiProxy(
    string Name,
    string Type,
    int? Delay);

public sealed record ClashTraffic(
    long Up,
    long Down,
    long UpTotal,
    long DownTotal);
