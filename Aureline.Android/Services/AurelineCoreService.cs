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
    private MemoryPressureMaintenance? memoryMaintenance;
    private ClashStatus status = ClashStatus.Stopped;

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Info(Tag, "OnCreate");
        MemoryPressure.ConfigureNativeRuntime();
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
        StopMemoryMaintenance();
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
                SendResponse(replyTo, requestId, responsePayload);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"IPC command failed. command={command}, error={ex}");
                SendResponse(replyTo, requestId, Fail(ex.Message));
            }
        });
    }

    private CoreIpcResponse Execute(string command, string payload)
    {
        return command switch
        {
            AndroidIpcCommands.Initialize => Ok(Initialize(payload)),
            AndroidIpcCommands.ValidateConfig => Ok(ValidateConfig(payload)),
            AndroidIpcCommands.Start => Ok(StartCore(payload)),
            AndroidIpcCommands.Stop => Ok(StopCore()),
            AndroidIpcCommands.GetStatus => Ok(SerializeStatus()),
            AndroidIpcCommands.GetProxyGroups => Ok(QueryProxyGroups(payload)),
            AndroidIpcCommands.GetTraffic => QueryTraffic(),
            AndroidIpcCommands.GetConnectionCount => Ok(intValue: LibClashNative.QueryConnectionCount()),
            AndroidIpcCommands.SelectProxy => Ok(boolValue: SelectProxy(payload)),
            AndroidIpcCommands.SetMode => Ok(boolValue: SetMode(payload)),
            AndroidIpcCommands.TestProxyDelay => Ok(intValue: TestProxyDelay(payload)),
            AndroidIpcCommands.HealthCheck => Ok(HealthCheck(payload)),
            AndroidIpcCommands.HealthCheckAll => Ok(HealthCheckAll()),
            AndroidIpcCommands.CloseAllConnections => Ok(CloseAllConnections()),
            AndroidIpcCommands.ForceGc => Ok(ForceGc()),
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

            if (ShouldStartListener(profile))
            {
                LibClashNative.StartListener();
            }
            else
            {
                TryStopListener();
            }

            if (profile.EnableTun)
            {
                AurelineVpnService.Start(this, ClashVpnOptions.FromProfile(profile));
            }

            StartMemoryMaintenance();
            MemoryPressure.Trim();
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
        StopMemoryMaintenance();
        MemoryPressure.Trim();
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
        var groupsJson = LibClashNative.QueryGroups(nativeSortMode);
        return string.IsNullOrWhiteSpace(groupsJson) ? "[]" : groupsJson;
    }

    private static CoreIpcResponse QueryTraffic()
    {
        return Ok(
            longValue: LibClashNative.QueryTrafficNow(),
            secondLongValue: LibClashNative.QueryTrafficTotal());
    }

    private static bool SelectProxy(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.SelectProxyIpcRequest);
        return request != null && LibClashNative.PatchSelector(request.GroupName, request.ProxyName);
    }

    private static bool SetMode(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.SetModeIpcRequest);
        return request != null && LibClashNative.SetMode(request.Mode);
    }

    private static int TestProxyDelay(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            AndroidIpcJsonContext.Default.ProxyDelayIpcRequest);
        if (request == null)
        {
            return 0;
        }

        var delay = LibClashNative.TestProxyDelay(request.ProxyName, request.TestUrl, request.TimeoutMilliseconds);
        return delay ?? 0;
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

    private static string ForceGc()
    {
        LibClashNative.ForceGc();
        return string.Empty;
    }

    private void InitializeCore(ClashProfile profile)
    {
        Log.Info(Tag, $"Initialize home={profile.HomeDirectory}, config={profile.ConfigPath}, sdk={(int)Build.VERSION.SdkInt}");
        MemoryPressure.ConfigureNativeRuntime();
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
                ShouldStartListener(profile) && profile.ExternalController ? ExternalControllerListenAt : string.Empty,
                profile.MixedPort,
                "https://www.gstatic.com/generate_204"),
            LibClashSetupJsonContext.Default.LibClashSetupRequest);
        var setupMessage = LibClashNative.SetupConfig(setupJson);
        return string.IsNullOrWhiteSpace(setupMessage) ? string.Empty : setupMessage;
    }

    private static bool ShouldStartListener(ClashProfile profile)
    {
        return !profile.EnableTun || profile.SystemProxy;
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

    private void StartMemoryMaintenance()
    {
        memoryMaintenance?.Dispose();
        memoryMaintenance = new MemoryPressureMaintenance();
    }

    private void StopMemoryMaintenance()
    {
        memoryMaintenance?.Dispose();
        memoryMaintenance = null;
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

    private static void SendResponse(Messenger? replyTo, long requestId, CoreIpcResponse ipcResponse)
    {
        if (replyTo == null)
        {
            return;
        }

        var response = Message.Obtain(null, AndroidIpcWire.MessageResponse) ??
            throw new InvalidOperationException("Failed to allocate IPC response message");
        var data = new Bundle();
        var responsePayload = JsonSerializer.Serialize(
            ipcResponse,
            AndroidIpcJsonContext.Default.CoreIpcResponse);
        data.PutLong(AndroidIpcWire.ExtraRequestId, requestId);
        data.PutString(AndroidIpcWire.ExtraPayload, responsePayload);
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

    private static CoreIpcResponse Ok(
        string payload = "",
        long longValue = 0,
        long secondLongValue = 0,
        int intValue = 0,
        bool boolValue = false)
    {
        return new CoreIpcResponse
        {
            Ok = true,
            Payload = payload,
            LongValue = longValue,
            SecondLongValue = secondLongValue,
            IntValue = intValue,
            BoolValue = boolValue
        };
    }

    private static CoreIpcResponse Fail(string error)
    {
        return new CoreIpcResponse
        {
            Ok = false,
            Error = error
        };
    }

    private sealed class CoreRequestHandler(AurelineCoreService service) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message msg)
        {
            service.HandleRequest(msg);
        }
    }
}
