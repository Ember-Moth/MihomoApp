using System.Net;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Java.Net;
using Aureline.Android.Interop;
using Aureline.Android.Services;
using VpnBuilder = Android.Net.VpnService.Builder;

namespace Aureline.Android.Vpn;

[Register("com.embermoth.aureline.AurelineVpnService")]
public sealed class AurelineVpnService : VpnService, IAndroidTunCallbacks
{
    public const string ActionStart = "com.embermoth.aureline.action.START_VPN";
    public const string ActionStop = "com.embermoth.aureline.action.STOP_VPN";

    private const string Tag = nameof(AurelineVpnService);
    private const int NotificationId = 1001;
    private const string NotificationChannelId = "aureline-vpn";
    private const string Ipv4Address = "172.19.0.1";
    private const string VpnDns = "1.1.1.1";
    private const string Ipv6Address = "fdfe:dcba:9876::1";

    private ParcelFileDescriptor? tunDescriptor;
    private ConnectivityManager? connectivityManager;
    private CancellationTokenSource? tunStartCancellation;
    private CancellationTokenSource? notificationUpdateCancellation;

    internal static void Start(Context context, ClashVpnOptions options)
    {
        var intent = new Intent(context, typeof(AurelineVpnService));
        intent.SetAction(ActionStart);
        options.PutInto(intent);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    internal static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(AurelineVpnService));
        intent.SetAction(ActionStop);
        context.StartService(intent);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Info(Tag, "OnCreate");
        StartVpnForeground("Aureline VPN is starting");
        connectivityManager = GetSystemService(ConnectivityService) as ConnectivityManager;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Info(Tag, $"OnStartCommand action={intent?.Action ?? "<null>"} startId={startId}");
        switch (intent?.Action)
        {
            case ActionStart:
                try
                {
                    StartVpn(ClashVpnOptions.FromIntent(intent));
                }
                catch (Exception ex)
                {
                    Log.Error(Tag, ex.ToString());
                    StopVpn();
                    return StartCommandResult.NotSticky;
                }

                return StartCommandResult.Sticky;
            case ActionStop:
                StopVpn();
                return StartCommandResult.NotSticky;
            default:
                return StartCommandResult.NotSticky;
        }
    }

    public override void OnDestroy()
    {
        Log.Info(Tag, "OnDestroy");
        StopActiveTun();
        base.OnDestroy();
    }

    bool IAndroidTunCallbacks.Protect(int fd)
    {
        return base.Protect(fd);
    }

    public int QuerySocketUid(int protocol, string source, string target)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && connectivityManager != null)
        {
            try
            {
                return connectivityManager.GetConnectionOwnerUid(
                    protocol,
                    ParseSocketAddress(source),
                    ParseSocketAddress(target));
            }
            catch
            {
                return -1;
            }
        }

        return -1;
    }

    public string ResolveProcess(int protocol, string source, string target, int uid)
    {
        var resolvedUid = uid > 0 ? uid : QuerySocketUid(protocol, source, target);
        if (resolvedUid < 0)
        {
            return string.Empty;
        }

        try
        {
            var packages = PackageManager?.GetPackagesForUid(resolvedUid);
            return packages?.FirstOrDefault(static packageName => !string.IsNullOrWhiteSpace(packageName))
                ?? $"uid:{resolvedUid}";
        }
        catch
        {
            return $"uid:{resolvedUid}";
        }
    }

    private void StartVpn(ClashVpnOptions options)
    {
        Log.Info(Tag, $"StartVpn stack={options.Stack}, ipv6={options.EnableIpv6}, systemProxy={options.SystemProxy}, routes={options.RouteAddressCsv}");
        StopActiveTun();
        StartVpnForeground("Aureline VPN is connecting");
        LibClashNative.SetAndroidCallbacks(this);

        var builder = new VpnBuilder(this)
            .SetSession("Aureline")
            .SetMtu(9000)
            .AddAddress(Ipv4Address, 30)
            .AddDnsServer(VpnDns);

        AddRoutes(builder, options.RouteAddressCsv);

        if (options.EnableIpv6)
        {
            builder.AddAddress(Ipv6Address, 126);
            AddIpv6DefaultRoute(builder, options.RouteAddressCsv);
        }

        ApplyAccessControl(builder, options);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            builder.SetMetered(false);
        }

        Log.Info(Tag, "Establishing Android VPN interface");
        tunDescriptor = builder.Establish() ?? throw new InvalidOperationException("Android rejected VPN establishment");
        var fd = tunDescriptor.DetachFd();
        tunDescriptor = null;
        Log.Info(Tag, $"VPN interface established. fd={fd}");

        tunStartCancellation = new CancellationTokenSource();
        var cancellationToken = tunStartCancellation.Token;
        _ = Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyncAndroidUidPackages();
                cancellationToken.ThrowIfCancellationRequested();
                Log.Info(Tag, "Starting libclash TUN");
                LibClashNative.StartTun(fd, options.Stack, TunAddressCsv(options), TunDnsCsv(options));
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Info(Tag, "libclash TUN is running");
                    StartVpnForeground(SpeedNotificationText(0, 0));
                    StartSpeedNotificationUpdates();
                }
            }
            catch (System.OperationCanceledException)
            {
                Log.Info(Tag, "TUN start canceled");
                LibClashNative.StopTun();
            }
            catch (Exception ex)
            {
                Log.Error(Tag, ex.ToString());
                StopVpn();
            }
        }, cancellationToken);
    }

    private void SyncAndroidUidPackages()
    {
        var packageManager = PackageManager;
        if (packageManager == null)
        {
            return;
        }

        try
        {
#pragma warning disable CA1422
#pragma warning disable CS0618
            var packages = packageManager.GetInstalledPackages(PackageInfoFlags.MetaData);
#pragma warning restore CS0618
#pragma warning restore CA1422
            var mapping = packages
                .Select(package => new
                {
                    Uid = package.ApplicationInfo?.Uid ?? -1,
                    PackageName = package.PackageName ?? string.Empty,
                })
                .Where(package => package.Uid >= 0 &&
                    !string.IsNullOrWhiteSpace(package.PackageName))
                .GroupBy(package => package.Uid)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(package => package.PackageName)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());

            var payloadJson = JsonSerializer.Serialize(
                mapping,
                LibClashSetupJsonContext.Default.DictionaryInt32StringArray);
            if (!LibClashNative.TryUpdateAndroidUidPackages(payloadJson))
            {
                Log.Debug(Tag, "libclash Android UID package mapping API is unavailable");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to sync Android UID package mapping: {ex}");
        }
    }

    private void StopVpn()
    {
        Log.Info(Tag, "StopVpn");
        StopActiveTun();
        StopForegroundCompat();
        StopSelf();
    }

    private void StopActiveTun()
    {
        Log.Info(Tag, "StopActiveTun");
        StopSpeedNotificationUpdates();

        tunStartCancellation?.Cancel();
        tunStartCancellation?.Dispose();
        tunStartCancellation = null;

        try
        {
            LibClashNative.StopTun();
        }
        catch
        {
            // Native library may not be present during early app development.
        }

        tunDescriptor?.Close();
        tunDescriptor = null;
    }

    private void StopForegroundCompat()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CS0618
            StopForeground(true);
#pragma warning restore CS0618
        }
    }

    private void StartVpnForeground(string text)
    {
        var notification = BuildNotification(text);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeManifest);
            return;
        }

        StartForeground(NotificationId, notification);
    }

    private Notification BuildNotification(string text)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            var channel = new NotificationChannel(
                NotificationChannelId,
                "Aureline VPN",
                NotificationImportance.Low);
            manager.CreateNotificationChannel(channel);
        }

        var intent = new Intent(this, typeof(MainActivity));
        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        var pendingIntent = PendingIntent.GetActivity(this, 0, intent, flags);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, NotificationChannelId)
            : new Notification.Builder(this);

        return builder
            .SetContentTitle("Aureline")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetLocalOnly(true)
            .SetContentIntent(pendingIntent)
            .Build();
    }

    private void StartSpeedNotificationUpdates()
    {
        StopSpeedNotificationUpdates();
        notificationUpdateCancellation = new CancellationTokenSource();
        var cancellationToken = notificationUpdateCancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var (upload, download) = UnpackTraffic(LibClashNative.QueryTrafficNow());
                    UpdateVpnNotification(SpeedNotificationText(upload, download));
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug(Tag, $"Failed to update VPN notification traffic: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    private void StopSpeedNotificationUpdates()
    {
        notificationUpdateCancellation?.Cancel();
        notificationUpdateCancellation?.Dispose();
        notificationUpdateCancellation = null;
    }

    private void UpdateVpnNotification(string text)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(NotificationId, BuildNotification(text));
    }

    private static string SpeedNotificationText(long upload, long download)
    {
        return $"↑ {FormatBytes(upload)}/s  ↓ {FormatBytes(download)}/s";
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

    private static void AddRoutes(VpnBuilder builder, string routeAddressCsv)
    {
        var routes = routeAddressCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (routes.Length == 0)
        {
            builder.AddRoute("0.0.0.0", 0);
            return;
        }

        foreach (var route in routes)
        {
            var parts = route.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix))
            {
                continue;
            }

            builder.AddRoute(parts[0], prefix);
        }
    }

    private static void AddIpv6DefaultRoute(VpnBuilder builder, string routeAddressCsv)
    {
        var hasIpv6Route = routeAddressCsv
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(route => route.Contains(':', StringComparison.Ordinal));
        if (!hasIpv6Route)
        {
            builder.AddRoute("::", 0);
        }
    }

    private void ApplyAccessControl(VpnBuilder builder, ClashVpnOptions options)
    {
        if (!options.AccessControlEnabled)
        {
            return;
        }

        var packageNames = options.AccessPackageNames
            .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (options.AccessControlMode == "acceptSelected")
        {
            foreach (var packageName in packageNames.Append(PackageName ?? string.Empty))
            {
                TryAddAllowedApplication(builder, packageName);
            }

            return;
        }

        foreach (var packageName in packageNames.Where(packageName =>
            !string.Equals(packageName, PackageName, StringComparison.Ordinal)))
        {
            TryAddDisallowedApplication(builder, packageName);
        }
    }

    private static void TryAddAllowedApplication(VpnBuilder builder, string packageName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                builder.AddAllowedApplication(packageName);
            }
        }
        catch (PackageManager.NameNotFoundException)
        {
        }
    }

    private static void TryAddDisallowedApplication(VpnBuilder builder, string packageName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                builder.AddDisallowedApplication(packageName);
            }
        }
        catch (PackageManager.NameNotFoundException)
        {
        }
    }

    private static string TunAddressCsv(ClashVpnOptions options)
    {
        return options.EnableIpv6
            ? $"{Ipv4Address}/30,{Ipv6Address}/126"
            : $"{Ipv4Address}/30";
    }

    private static string TunDnsCsv(ClashVpnOptions options)
    {
        if (options.DnsHijacking)
        {
            return "0.0.0.0";
        }

        return VpnDns;
    }

    private static InetSocketAddress ParseSocketAddress(string endpoint)
    {
        var parsed = IPEndPoint.Parse(endpoint);
        return new InetSocketAddress(parsed.Address.ToString(), parsed.Port);
    }
}
