using Avalonia;
using Avalonia.iOS;
using Foundation;
using Mihomo.iOS.Services;

namespace Mihomo.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<global::Mihomo.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        IosClashRuntime.Install();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
