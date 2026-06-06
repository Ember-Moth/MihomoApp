using System.Runtime;
using System.Threading;
using Android.Util;
using Aureline.Android.Interop;

namespace Aureline.Android.Services;

internal static class MemoryPressure
{
    private const string Tag = nameof(MemoryPressure);
    private const long NativeCoreMemoryLimitBytes = 16L * 1024L * 1024L;
    private const int NativeCoreGcPercent = 10;

    private static int isTrimming;

    public static void ConfigureNativeRuntime()
    {
        var previousLimit = LibClashNative.SetMemoryLimit(NativeCoreMemoryLimitBytes);
        var previousGcPercent = LibClashNative.SetGcPercent(NativeCoreGcPercent);
        Log.Debug(
            Tag,
            $"Configured native memory. limit={NativeCoreMemoryLimitBytes}, previousLimit={previousLimit?.ToString() ?? "<unavailable>"}, gcPercent={NativeCoreGcPercent}, previousGcPercent={previousGcPercent?.ToString() ?? "<unavailable>"}");
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
            catch (Exception ex)
            {
                Log.Debug(Tag, $"Managed memory trim failed: {ex.Message}");
            }

            LibClashNative.ForceGc();
            return true;
        }
        finally
        {
            Volatile.Write(ref isTrimming, 0);
        }
    }
}
