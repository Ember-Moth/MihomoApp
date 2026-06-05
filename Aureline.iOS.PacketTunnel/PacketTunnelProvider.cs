using Foundation;
using NetworkExtension;
using Aureline.iOS.PacketTunnel.Services;

namespace Aureline.iOS.PacketTunnel;

[Register("PacketTunnelProvider")]
public sealed class PacketTunnelProvider : NEPacketTunnelProvider
{
    private PacketTunnelRuntime? runtime;

#pragma warning disable CS8610
    public override void StartTunnel(NSDictionary<NSString, NSObject>? options, Action<NSError?> completionHandler)
    {
        runtime = new PacketTunnelRuntime(this);
        runtime.Start(options ?? new NSDictionary<NSString, NSObject>(), completionHandler);
    }
#pragma warning restore CS8610

    public override void StopTunnel(NEProviderStopReason reason, Action completionHandler)
    {
        runtime?.Stop();
        runtime = null;
        completionHandler();
    }

#pragma warning disable CS8610
    public override void HandleAppMessage(NSData messageData, Action<NSData?> completionHandler)
    {
        var response = runtime?.HandleAppMessage(messageData) ??
            NSData.FromArray("{\"ok\":false,\"error\":\"packet tunnel runtime is not running\"}"u8.ToArray());
        completionHandler(response);
    }
#pragma warning restore CS8610
}
