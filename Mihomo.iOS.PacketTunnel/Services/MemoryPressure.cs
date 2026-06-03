using System.Runtime;
using Mihomo.iOS.PacketTunnel.Interop;

namespace Mihomo.iOS.PacketTunnel.Services;

internal static class MemoryPressure
{
    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch
        {
            // Memory trimming is best-effort in the extension process.
        }

        try
        {
            LibClashNative.ForceGc();
        }
        catch
        {
            // The native core may not have been initialized yet.
        }
    }
}
