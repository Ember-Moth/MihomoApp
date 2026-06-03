using Avalonia;
using Avalonia.iOS;
using Foundation;
using Mihomo.iOS.Services;

namespace Mihomo.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<global::Mihomo.App>
{
    public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary launchOptions)
    {
        IosClashRuntime.Install();
        return base.FinishedLaunching(application, launchOptions);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
