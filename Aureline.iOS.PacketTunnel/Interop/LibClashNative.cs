using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aureline.iOS.PacketTunnel.Interop;

internal static partial class LibClashNative
{
    private const string Library = "__Internal";

    [LibraryImport(Library, EntryPoint = "libclash_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeInit(string homeDir, int platformVersion);

    [LibraryImport(Library, EntryPoint = "libclash_validate_config", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeValidateConfig(string configPath);

    [LibraryImport(Library, EntryPoint = "libclash_setup_config", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeSetupConfig(string setupJson);

    [LibraryImport(Library, EntryPoint = "libclash_start_listener")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStartListener();

    [LibraryImport(Library, EntryPoint = "libclash_stop_listener")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStopListener();

    [LibraryImport(Library, EntryPoint = "libclash_reset")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeReset();

    [LibraryImport(Library, EntryPoint = "libclash_query_traffic_now")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long NativeQueryTrafficNow();

    [LibraryImport(Library, EntryPoint = "libclash_query_traffic_total")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long NativeQueryTrafficTotal();

    [LibraryImport(Library, EntryPoint = "libclash_query_connection_count")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeQueryConnectionCount();

    [LibraryImport(Library, EntryPoint = "libclash_close_all_connections")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeCloseAllConnections();

    [LibraryImport(Library, EntryPoint = "libclash_set_mode", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeSetMode(string mode);

    [LibraryImport(Library, EntryPoint = "libclash_query_group_names")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeQueryGroupNames(int excludeNotSelectable);

    [LibraryImport(Library, EntryPoint = "libclash_query_group", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeQueryGroup(string name, string sortMode);

    [LibraryImport(Library, EntryPoint = "libclash_patch_selector", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativePatchSelector(string selector, string name);

    [LibraryImport(Library, EntryPoint = "libclash_health_check", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeHealthCheck(string name);

    [LibraryImport(Library, EntryPoint = "libclash_health_check_all")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeHealthCheckAll();

    [LibraryImport(Library, EntryPoint = "libclash_test_proxy_delay", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeTestProxyDelay(string name, string testUrl, int timeoutMilliseconds);

    [LibraryImport(Library, EntryPoint = "libclash_force_gc")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeForceGc();

    [LibraryImport(Library, EntryPoint = "libclash_set_memory_limit")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long NativeSetMemoryLimit(long bytes);

    [LibraryImport(Library, EntryPoint = "libclash_set_gc_percent")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeSetGcPercent(int percent);

    [LibraryImport(Library, EntryPoint = "libclash_start_tun", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStartTun(
        int fd,
        string stack,
        string addressCsv,
        string dnsCsv);

    [LibraryImport(Library, EntryPoint = "libclash_stop_tun")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeStopTun();

    [LibraryImport(Library, EntryPoint = "libclash_update_dns", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeUpdateDns(string dnsCsv);

    [LibraryImport(Library, EntryPoint = "libclash_last_error")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeLastError();

    [LibraryImport(Library, EntryPoint = "libclash_free_string")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeFreeString(IntPtr value);

    public static void Init(string homeDir)
    {
        ThrowIfFalse(NativeInit(homeDir, 0), "libclash_init failed");
    }

    public static string ValidateConfig(string configPath)
    {
        return TakeNativeString(NativeValidateConfig(configPath));
    }

    public static string SetupConfig(string setupJson)
    {
        return TakeNativeString(NativeSetupConfig(setupJson));
    }

    public static void StartListener()
    {
        ThrowIfFalse(NativeStartListener(), "libclash_start_listener failed");
    }

    public static void StopListener()
    {
        ThrowIfFalse(NativeStopListener(), "libclash_stop_listener failed");
    }

    public static void Reset()
    {
        NativeReset();
    }

    public static long QueryTrafficNow()
    {
        return NativeQueryTrafficNow();
    }

    public static long QueryTrafficTotal()
    {
        return NativeQueryTrafficTotal();
    }

    public static int QueryConnectionCount()
    {
        return NativeQueryConnectionCount();
    }

    public static void CloseAllConnections()
    {
        NativeCloseAllConnections();
    }

    public static bool SetMode(string mode)
    {
        return NativeSetMode(mode) != 0;
    }

    public static string QueryGroupNames(bool excludeNotSelectable)
    {
        return TakeNativeString(NativeQueryGroupNames(excludeNotSelectable ? 1 : 0));
    }

    public static string QueryGroup(string name, string sortMode)
    {
        return TakeNativeString(NativeQueryGroup(name, sortMode));
    }

    public static bool PatchSelector(string selector, string name)
    {
        return NativePatchSelector(selector, name) != 0;
    }

    public static void HealthCheck(string name)
    {
        NativeHealthCheck(name);
    }

    public static void HealthCheckAll()
    {
        NativeHealthCheckAll();
    }

    public static int? TestProxyDelay(string name, string testUrl, int timeoutMilliseconds)
    {
        var delay = NativeTestProxyDelay(name, testUrl, timeoutMilliseconds);
        return delay > 0 ? delay : null;
    }

    public static void ForceGc()
    {
        NativeForceGc();
    }

    public static long SetMemoryLimit(long bytes)
    {
        return NativeSetMemoryLimit(bytes);
    }

    public static int SetGcPercent(int percent)
    {
        return NativeSetGcPercent(percent);
    }

    public static void StartTun(int fd, string stack, string addressCsv, string dnsCsv)
    {
        ThrowIfFalse(NativeStartTun(fd, stack, addressCsv, dnsCsv), "libclash_start_tun failed");
    }

    public static void StopTun()
    {
        NativeStopTun();
    }

    public static void UpdateDns(string dnsCsv)
    {
        NativeUpdateDns(dnsCsv);
    }

    private static string TakeNativeString(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUTF8(value) ?? string.Empty;
        }
        finally
        {
            NativeFreeString(value);
        }
    }

    private static void ThrowIfFalse(int value, string message)
    {
        if (value == 0)
        {
            var detail = TakeNativeString(NativeLastError());
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}");
        }
    }
}
