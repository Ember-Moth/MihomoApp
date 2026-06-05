using Avalonia;
using Avalonia.iOS;
using Foundation;
using Aureline.iOS.Services;

namespace Aureline.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<global::Aureline.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        IosClashRuntime.Install();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
