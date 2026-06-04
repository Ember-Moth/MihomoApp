using System.Runtime;
using System.Threading;
using Mihomo.iOS.PacketTunnel.Interop;

namespace Mihomo.iOS.PacketTunnel.Services;

internal static class MemoryPressure
{
    private const long NativeCoreMemoryLimitBytes = 32L * 1024L * 1024L;
    private const int NativeCoreGcPercent = 50;

    private static int isTrimming;

    public static void ConfigureNativeRuntime()
    {
        try
        {
            LibClashNative.SetMemoryLimit(NativeCoreMemoryLimitBytes);
            LibClashNative.SetGcPercent(NativeCoreGcPercent);
        }
        catch
        {
            // The native core may not be linked in simulator-only diagnostics.
        }
    }

    public static bool Trim()
    {
        if (Interlocked.Exchange(ref isTrimming, 1) != 0)
        {
            return false;
        }

        try
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

            return true;
        }
        finally
        {
            Volatile.Write(ref isTrimming, 0);
        }
    }
}
