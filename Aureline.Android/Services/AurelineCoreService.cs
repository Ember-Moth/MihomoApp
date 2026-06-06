using System.Globalization;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Aureline.Android.Interop;
using Aureline.Android.Vpn;
using Aureline.Models;
using Aureline.Services.Clash;

namespace Aureline.Android.Services;

[Register("com.embermoth.aureline.AurelineCoreService")]
public sealed class AurelineCoreService : Service
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";
    private const string Tag = nameof(AurelineCoreService);

    private Messenger? requestMessenger;
    private ClashStatus status = ClashStatus.Stopped;

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Info(Tag, "OnCreate");
        requestMessenger = new Messenger(new CoreRequestHandler(this));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Info(Tag, $"OnStartCommand action={intent?.Action ?? "<null>"} startId={startId}");
        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent)
    {
        if (intent?.Action == AndroidIpcWire.ActionBindCore)
        {
            return requestMessenger?.Binder;
        }

        return null;
    }

    public override void OnDestroy()
    {
        Log.Info(Tag, "OnDestroy");
        try
        {
            StopCore();
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to stop core during destroy: {ex}");
        }

        requestMessenger?.Dispose();
        requestMessenger = null;
        base.OnDestroy();
    }

    private void HandleRequest(Message message)
    {
        if (message.What != AndroidIpcWire.MessageRequest)
        {
            return;
        }

        var data = message.Data;
        if (data == null)
        {
            return;
        }

        var requestId = data.GetLong(AndroidIpcWire.ExtraRequestId);
        var command = data.GetString(AndroidIpcWire.ExtraCommand) ?? string.Empty;
        var payload = data.GetString(AndroidIpcWire.ExtraPayload) ?? string.Empty;
        var replyTo = message.ReplyTo;

        _ = Task.Run(() =>
        {
            try
            {
                var responsePayload = Execute(command, payload);
                SendResponse(replyTo, requestId, responsePayload, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"IPC command failed. command={command}, error={ex}");
                SendResponse(replyTo, requestId, string.Empty, ex.Message);
            }
        });
    }

    private string Execute(string command, string payload)
    {
        return command switch
        {
            AndroidIpcCommands.Initialize => Initialize(payload),
            AndroidIpcCommands.ValidateConfig => ValidateConfig(payload),
            AndroidIpcCommands.Start => StartCore(payload),
            AndroidIpcCommands.Stop => StopCore(),
            AndroidIpcCommands.GetStatus => SerializeStatus(),
            AndroidIpcCommands.GetProxyGroups => QueryProxyGroups(payload),
            AndroidIpcCommands.GetTraffic => QueryTraffic(),
            AndroidIpcCommands.GetConnectionCount => QueryConnectionCount(),
            AndroidIpcCommands.SelectProxy => SelectProxy(payload),
            AndroidIpcCommands.SetMode => SetMode(payload),
            AndroidIpcCommands.TestProxyDelay => TestProxyDelay(payload),
            AndroidIpcCommands.HealthCheck => HealthCheck(payload),
            AndroidIpcCommands.HealthCheckAll => HealthCheckAll(),
            AndroidIpcCommands.CloseAllConnections => CloseAllConnections(),
            _ => throw new InvalidOperationException($"Unknown IPC command: {command}")
        };
    }

    private string Initialize(string payload)
    {
        var profile = DeserializeProfile(payload);
        InitializeCore(profile);
        return string.Empty;
    }

    private string ValidateConfig(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.ValidateConfigIpcRequest);
        var configPath = request?.ConfigPath ?? string.Empty;

        Log.Info(Tag, $"Validate config={configPath}");
        if (!File.Exists(configPath))
        {
            return $"Config file not found: {configPath}";
        }

        return LibClashNative.ValidateConfig(configPath);
    }

    private string StartCore(string payload)
    {
        var profile = DeserializeProfile(payload);
        Publish(new ClashStatus(ClashRunState.Starting, "正在启动核心"));

        try
        {
            Log.Info(Tag, $"StartCore tun={profile.EnableTun}, config={profile.ConfigPath}");
            InitializeCore(profile);
            var setupMessage = SetupCore(profile);
            if (!string.IsNullOrWhiteSpace(setupMessage))
            {
                Publish(new ClashStatus(ClashRunState.Error, setupMessage));
                throw new InvalidOperationException(setupMessage);
            }

            LibClashNative.StartListener();

            if (profile.EnableTun)
            {
                AurelineVpnService.Start(this, ClashVpnOptions.FromProfile(profile));
            }

            Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, ex.ToString());
            Publish(new ClashStatus(ClashRunState.Error, ex.Message));
            throw;
        }
    }

    private string StopCore()
    {
        Publish(new ClashStatus(ClashRunState.Stopping, "Stopping"));
        try
        {
            AurelineVpnService.Stop(this);
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to stop VPN service: {ex.Message}");
        }

        TryStopListener();
        TryResetCore();
        Publish(ClashStatus.Stopped);
        return string.Empty;
    }

    private string SerializeStatus()
    {
        return JsonSerializer.Serialize(status, AndroidIpcJsonContext.Default.ClashStatus);
    }

    private string QueryProxyGroups(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.ProxyGroupsIpcRequest);
        var nativeSortMode = ToNativeSortMode(request?.SortMode ?? string.Empty);
        var namesJson = LibClashNative.QueryGroupNames(false);
        var names = JsonSerializer.Deserialize(
            namesJson,
            LibClashSetupJsonContext.Default.ListString) ?? [];
        var groups = new List<ClashProxyGroup>(names.Count);

        foreach (var name in names)
        {
            var groupJson = LibClashNative.QueryGroup(name, nativeSortMode);
            if (string.IsNullOrWhiteSpace(groupJson))
            {
                continue;
            }

            var group = JsonSerializer.Deserialize(
                groupJson,
                LibClashSetupJsonContext.Default.NativeProxyGroup);
            if (group?.Proxies is not { Count: > 0 } proxies)
            {
                continue;
            }

            var nodes = proxies
                .Where(proxy => !string.IsNullOrWhiteSpace(proxy.Name))
                .Select(proxy => new ClashProxy(
                    proxy.Name ?? string.Empty,
                    proxy.Type ?? proxy.Subtitle ?? string.Empty,
                    string.Empty,
                    proxy.Delay > 0 && proxy.Delay < ushort.MaxValue ? proxy.Delay : null))
                .ToArray();

            groups.Add(new ClashProxyGroup(
                name,
                group.Type ?? string.Empty,
                group.Now ?? string.Empty,
                string.Empty,
                nodes));
        }

        return JsonSerializer.Serialize(groups.ToArray(), AndroidIpcJsonContext.Default.ClashProxyGroupArray);
    }

    private static string QueryTraffic()
    {
        var now = UnpackTraffic(LibClashNative.QueryTrafficNow());
        var total = UnpackTraffic(LibClashNative.QueryTrafficTotal());
        var traffic = new ClashTraffic(now.Upload, now.Download, total.Upload, total.Download);
        return JsonSerializer.Serialize(traffic, AndroidIpcJsonContext.Default.ClashTraffic);
    }

    private static string QueryConnectionCount()
    {
        return LibClashNative.QueryConnectionCount().ToString(CultureInfo.InvariantCulture);
    }

    private static string SelectProxy(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.SelectProxyIpcRequest);
        var ok = request != null && LibClashNative.PatchSelector(request.GroupName, request.ProxyName);
        return ok ? "1" : "0";
    }

    private static string SetMode(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.SetModeIpcRequest);
        var ok = request != null && LibClashNative.SetMode(request.Mode);
        return ok ? "1" : "0";
    }

    private static string TestProxyDelay(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.ProxyDelayIpcRequest);
        if (request == null)
        {
            return string.Empty;
        }

        var delay = LibClashNative.TestProxyDelay(request.ProxyName, request.TestUrl, request.TimeoutMilliseconds);
        return delay?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string HealthCheck(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.HealthCheckIpcRequest);
        if (string.IsNullOrWhiteSpace(request?.GroupName))
        {
            LibClashNative.HealthCheckAll();
            return string.Empty;
        }

        LibClashNative.HealthCheck(request.GroupName);
        return string.Empty;
    }

    private static string HealthCheckAll()
    {
        LibClashNative.HealthCheckAll();
        return string.Empty;
    }

    private static string CloseAllConnections()
    {
        LibClashNative.CloseAllConnections();
        return string.Empty;
    }

    private void InitializeCore(ClashProfile profile)
    {
        Log.Info(Tag, $"Initialize home={profile.HomeDirectory}, config={profile.ConfigPath}, sdk={(int)Build.VERSION.SdkInt}");
        Directory.CreateDirectory(profile.HomeDirectory);
        EnsureConfigFile(profile);
        LibClashNative.Init(profile.HomeDirectory, (int)Build.VERSION.SdkInt);
    }

    private static ClashProfile DeserializeProfile(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.AndroidCoreProfile);
        return request?.ToProfile() ?? throw new InvalidOperationException("Missing core profile payload");
    }

    private static string SetupCore(ClashProfile profile)
    {
        var setupJson = JsonSerializer.Serialize(
            new LibClashSetupRequest(
                profile.HomeDirectory,
                profile.ConfigPath,
                profile.ExternalController ? ExternalControllerListenAt : string.Empty,
                profile.MixedPort,
                "https://www.gstatic.com/generate_204"),
            LibClashSetupJsonContext.Default.LibClashSetupRequest);
        var setupMessage = LibClashNative.SetupConfig(setupJson);
        return string.IsNullOrWhiteSpace(setupMessage) ? string.Empty : setupMessage;
    }

    private static void EnsureConfigFile(ClashProfile profile)
    {
        var targetConfigPath = Path.Combine(profile.HomeDirectory, "config.yaml");
        if (!string.Equals(profile.ConfigPath, targetConfigPath, StringComparison.Ordinal) && File.Exists(profile.ConfigPath))
        {
            File.Copy(profile.ConfigPath, targetConfigPath, true);
            return;
        }

        if (File.Exists(targetConfigPath))
        {
            return;
        }

        File.WriteAllText(targetConfigPath, DefaultConfig(profile.MixedPort));
    }

    private static string DefaultConfig(int mixedPort)
    {
        return $$"""
               mixed-port: {{mixedPort}}
               allow-lan: false
               mode: rule
               log-level: info
               ipv6: false
               proxies:
                 - name: DIRECT-TEST
                   type: direct
               proxy-groups:
                 - name: Proxy
                   type: select
                   proxies:
                     - DIRECT
                     - DIRECT-TEST
               rules:
                 - MATCH,Proxy
               """;
    }

    private void Publish(ClashStatus nextStatus)
    {
        status = nextStatus;
        Log.Info(Tag, $"Status={nextStatus.State}, message={nextStatus.Message}");
    }

    private static void TryStopListener()
    {
        try
        {
            LibClashNative.StopListener();
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to stop listener: {ex.Message}");
        }
    }

    private static void TryResetCore()
    {
        try
        {
            LibClashNative.Reset();
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to reset core: {ex.Message}");
        }
    }

    private static string ToNativeSortMode(string sortMode)
    {
        return sortMode switch
        {
            "按延迟" => "Delay",
            "按名称" => "Title",
            _ => "Default"
        };
    }

    private static (long Upload, long Download) UnpackTraffic(long packed)
    {
        var upload = DecodeTrafficUnit((uint)((ulong)packed >> 32));
        var download = DecodeTrafficUnit((uint)((ulong)packed & uint.MaxValue));
        return (upload, download);
    }

    private static long DecodeTrafficUnit(uint value)
    {
        var unit = value >> 30;
        var amount = value & 0x3fffffff;
        return unit switch
        {
            1 => amount * 1024L / 100L,
            2 => amount * 1024L * 1024L / 100L,
            3 => amount * 1024L * 1024L * 1024L / 100L,
            _ => amount
        };
    }

    private static void SendResponse(Messenger? replyTo, long requestId, string payload, string error)
    {
        if (replyTo == null)
        {
            return;
        }

        var response = Message.Obtain(null, AndroidIpcWire.MessageResponse) ??
            throw new InvalidOperationException("Failed to allocate IPC response message");
        var data = new Bundle();
        data.PutLong(AndroidIpcWire.ExtraRequestId, requestId);
        data.PutBoolean(AndroidIpcWire.ExtraSuccess, string.IsNullOrWhiteSpace(error));
        data.PutString(AndroidIpcWire.ExtraPayload, payload);
        data.PutString(AndroidIpcWire.ExtraError, error);
        response.Data = data;

        try
        {
            replyTo.Send(response);
        }
        catch (RemoteException ex)
        {
            Log.Debug(Tag, $"Failed to send IPC response: {ex.Message}");
        }
        finally
        {
            response.Dispose();
            data.Dispose();
        }
    }

    private sealed class CoreRequestHandler(AurelineCoreService service) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message msg)
        {
            service.HandleRequest(msg);
        }
    }
}
