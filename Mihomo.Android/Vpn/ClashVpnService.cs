using System.Net;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Java.Net;
using Mihomo.Android.Interop;
using VpnBuilder = Android.Net.VpnService.Builder;

namespace Mihomo.Android.Vpn;

[Register("com.embermoth.mihomo.ClashVpnService")]
public sealed class ClashVpnService : VpnService, IAndroidTunCallbacks
{
    public const string ActionStart = "com.embermoth.mihomo.action.START_VPN";
    public const string ActionStop = "com.embermoth.mihomo.action.STOP_VPN";

    private const string Tag = nameof(ClashVpnService);
    private const int NotificationId = 1001;
    private const string NotificationChannelId = "mihomo-vpn";
    private const string Ipv4Address = "172.19.0.1";
    private const string Ipv4Dns = "172.19.0.2";
    private const string Ipv6Address = "fdfe:dcba:9876::1";
    private const string Ipv6Dns = "fdfe:dcba:9876::2";

    private ParcelFileDescriptor? tunDescriptor;
    private ConnectivityManager? connectivityManager;
    private CancellationTokenSource? tunStartCancellation;

    internal static void Start(Context context, ClashVpnOptions options)
    {
        var intent = new Intent(context, typeof(ClashVpnService));
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
        var intent = new Intent(context, typeof(ClashVpnService));
        intent.SetAction(ActionStop);
        context.StartService(intent);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Info(Tag, "OnCreate");
        StartVpnForeground("Mihomo VPN is starting");
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

    private void StartVpn(ClashVpnOptions options)
    {
        Log.Info(Tag, $"StartVpn stack={options.Stack}, ipv6={options.EnableIpv6}, systemProxy={options.SystemProxy}, routes={options.RouteAddressCsv}");
        StopActiveTun();
        StartVpnForeground("Mihomo VPN is connecting");
        LibClashNative.SetAndroidCallbacks(this);

        var builder = new VpnBuilder(this)
            .SetSession("Mihomo")
            .SetMtu(9000)
            .AddAddress(Ipv4Address, 30)
            .AddDnsServer(Ipv4Dns);

        AddRoutes(builder, options.RouteAddressCsv);

        if (options.EnableIpv6)
        {
            builder.AddAddress(Ipv6Address, 126);
            builder.AddDnsServer(Ipv6Dns);
        }

        ApplyAccessControl(builder, options);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            builder.SetMetered(false);
            if (options.SystemProxy)
            {
                var proxyInfo = ProxyInfo.BuildDirectProxy("127.0.0.1", options.MixedPort);
                if (proxyInfo != null)
                {
                    builder.SetHttpProxy(proxyInfo);
                }
            }
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
                Log.Info(Tag, "Starting libclash TUN");
                LibClashNative.StartTun(fd, options.Stack, TunAddressCsv(options), TunDnsCsv(options));
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Info(Tag, "libclash TUN is running");
                    StartVpnForeground("Mihomo core is running");
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
                "Mihomo VPN",
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
            .SetContentTitle("Mihomo")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetOngoing(true)
            .SetContentIntent(pendingIntent)
            .Build();
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
            return options.EnableIpv6 ? "0.0.0.0,::" : "0.0.0.0";
        }

        return options.EnableIpv6 ? $"{Ipv4Dns},{Ipv6Dns}" : Ipv4Dns;
    }

    private static InetSocketAddress ParseSocketAddress(string endpoint)
    {
        var parsed = IPEndPoint.Parse(endpoint);
        return new InetSocketAddress(parsed.Address.ToString(), parsed.Port);
    }
}
