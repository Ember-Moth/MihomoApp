using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Mihomo.Android;

[Application]
public class Application : AvaloniaAndroidApplication<global::Mihomo.App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
