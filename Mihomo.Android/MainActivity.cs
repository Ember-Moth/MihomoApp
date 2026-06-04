using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Mihomo.Android.Services;

namespace Mihomo.Android;

[Activity(
    Label = "Mihomo",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int RequestPostNotifications = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidClashRuntime.Install(this);
        base.OnCreate(savedInstanceState);
        RequestNotificationPermission();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == AndroidClashRuntime.RequestVpnPermission)
        {
            AndroidClashRuntime.Current?.OnVpnPermissionResult(resultCode == Result.Ok);
        }
    }

    private void RequestNotificationPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return;
        }

        RequestPermissions(
            [global::Android.Manifest.Permission.PostNotifications],
            RequestPostNotifications);
    }
}
