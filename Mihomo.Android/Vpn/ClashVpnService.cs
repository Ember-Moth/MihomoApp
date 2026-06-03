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
        StartVpnForeground("Mihomo VPN is starting");
        connectivityManager = GetSystemService(ConnectivityService) as ConnectivityManager;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActionStart:
                try
                {
                    StartVpn(ClashVpnOptions.FromIntent(intent));
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(ClashVpnService), ex.ToString());
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

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            builder.SetMetered(false);
        }

        tunDescriptor = builder.Establish() ?? throw new InvalidOperationException("Android rejected VPN establishment");
        var fd = tunDescriptor.DetachFd();
        tunDescriptor = null;

        tunStartCancellation = new CancellationTokenSource();
        var cancellationToken = tunStartCancellation.Token;
        _ = Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibClashNative.StartTun(fd, options.Stack, TunAddressCsv(options), TunDnsCsv(options));
                if (!cancellationToken.IsCancellationRequested)
                {
                    StartVpnForeground("Mihomo core is running");
                }
            }
            catch (System.OperationCanceledException)
            {
                LibClashNative.StopTun();
            }
            catch (Exception ex)
            {
                Log.Error(nameof(ClashVpnService), ex.ToString());
                StopVpn();
            }
        }, cancellationToken);
    }

    private void StopVpn()
    {
        StopActiveTun();
        StopForegroundCompat();
        StopSelf();
    }

    private void StopActiveTun()
    {
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
