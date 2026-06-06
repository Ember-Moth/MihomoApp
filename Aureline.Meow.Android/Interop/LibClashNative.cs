using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Aureline.Android.Interop;

internal interface IAndroidTunCallbacks
{
    bool Protect(int fd);

    int QuerySocketUid(int protocol, string source, string target);

    string ResolveProcess(int protocol, string source, string target, int uid);
}

internal static unsafe partial class LibClashNative
{
    private const string Library = "libmeow.so";
    private const string DefaultTestUrl = "https://www.gstatic.com/generate_204";
    private const int DefaultTimeoutMilliseconds = 5000;

    private static GCHandle? callbackHandle;

    private static delegate* unmanaged[Cdecl]<IntPtr, int, int> ProtectThunk => &HandleProtect;

    private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, int, IntPtr, int, int> ResolveProcessThunk =>
        &HandleResolveProcess;

    [LibraryImport(Library, EntryPoint = "libmeow_set_android_callbacks")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeSetAndroidCallbacks(
        IntPtr context,
        delegate* unmanaged[Cdecl]<IntPtr, int, int> protect,
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, int, IntPtr, int, int> resolveProcess);

    [LibraryImport(Library, EntryPoint = "libmeow_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeInit(
        string homeDir,
        int androidSdkVersion);

    [LibraryImport(Library, EntryPoint = "libmeow_validate_config", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeValidateConfig(string configPath);

    [LibraryImport(Library, EntryPoint = "libmeow_setup_config", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeSetupConfig(string setupJson);

    [LibraryImport(Library, EntryPoint = "libmeow_start_listener")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStartListener();

    [LibraryImport(Library, EntryPoint = "libmeow_stop_listener")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStopListener();

    [LibraryImport(Library, EntryPoint = "libmeow_reset")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeReset();

    [LibraryImport(Library, EntryPoint = "libmeow_query_traffic_now")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long NativeQueryTrafficNow();

    [LibraryImport(Library, EntryPoint = "libmeow_query_traffic_total")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long NativeQueryTrafficTotal();

    [LibraryImport(Library, EntryPoint = "libmeow_query_connection_count")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeQueryConnectionCount();

    [LibraryImport(Library, EntryPoint = "libmeow_close_all_connections")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeCloseAllConnections();

    [LibraryImport(Library, EntryPoint = "libmeow_set_mode", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeSetMode(string mode);

    [LibraryImport(Library, EntryPoint = "libmeow_query_group_names")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeQueryGroupNames();

    [LibraryImport(Library, EntryPoint = "libmeow_query_group", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeQueryGroup(string name);

    [LibraryImport(Library, EntryPoint = "libmeow_patch_selector", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativePatchSelector(string selector, string name);

    [LibraryImport(Library, EntryPoint = "libmeow_health_check", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeHealthCheck(string name, string url, int timeoutMilliseconds);

    [LibraryImport(Library, EntryPoint = "libmeow_health_check_all", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeHealthCheckAll(string url, int timeoutMilliseconds);

    [LibraryImport(Library, EntryPoint = "libmeow_test_proxy_delay", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeTestProxyDelay(string name, string testUrl, int timeoutMilliseconds);

    [LibraryImport(Library, EntryPoint = "libmeow_start_tun", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeStartTun(
        int fd,
        string stack,
        string addressCsv,
        string dnsCsv);

    [LibraryImport(Library, EntryPoint = "libmeow_stop_tun")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeStopTun();

    [LibraryImport(Library, EntryPoint = "libmeow_update_dns", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeUpdateDns(string dnsCsv);

    [LibraryImport(Library, EntryPoint = "libmeow_free_string")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeFreeString(IntPtr value);

    [LibraryImport(Library, EntryPoint = "libmeow_last_error")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeLastError();

    public static void SetAndroidCallbacks(IAndroidTunCallbacks callbacks)
    {
        if (callbackHandle is { IsAllocated: true } handle)
        {
            handle.Free();
        }

        callbackHandle = GCHandle.Alloc(callbacks);
        NativeSetAndroidCallbacks(GCHandle.ToIntPtr(callbackHandle.Value), ProtectThunk, ResolveProcessThunk);
    }

    public static bool TryUpdateAndroidUidPackages(string payloadJson)
    {
        _ = payloadJson;
        return false;
    }

    public static void Init(string homeDir, int androidSdkVersion)
    {
        ThrowIfFalse(NativeInit(homeDir, androidSdkVersion), "libmeow_init failed");
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
        ThrowIfFalse(NativeStartListener(), "libmeow_start_listener failed");
    }

    public static void StopListener()
    {
        ThrowIfFalse(NativeStopListener(), "libmeow_stop_listener failed");
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
        _ = excludeNotSelectable;
        return TakeNativeString(NativeQueryGroupNames());
    }

    public static string QueryGroup(string name, string sortMode)
    {
        _ = sortMode;
        return TakeNativeString(NativeQueryGroup(name));
    }

    public static bool PatchSelector(string selector, string name)
    {
        return NativePatchSelector(selector, name) != 0;
    }

    public static void HealthCheck(string name)
    {
        _ = TakeNativeString(NativeHealthCheck(name, DefaultTestUrl, DefaultTimeoutMilliseconds));
    }

    public static void HealthCheckAll()
    {
        _ = TakeNativeString(NativeHealthCheckAll(DefaultTestUrl, DefaultTimeoutMilliseconds));
    }

    public static int? TestProxyDelay(string name, string testUrl, int timeoutMilliseconds)
    {
        var delay = NativeTestProxyDelay(name, testUrl, timeoutMilliseconds);
        return delay > 0 ? delay : null;
    }

    public static void StartTun(int fd, string stack, string addressCsv, string dnsCsv)
    {
        ThrowIfFalse(NativeStartTun(fd, stack, addressCsv, dnsCsv), "libmeow_start_tun failed");
    }

    public static void StopTun()
    {
        NativeStopTun();
    }

    public static void UpdateDns(string dnsCsv)
    {
        NativeUpdateDns(dnsCsv);
    }

    public static void ForceGc()
    {
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandleProtect(IntPtr context, int fd)
    {
        return GetCallbacks(context).Protect(fd) ? 1 : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandleResolveProcess(
        IntPtr context,
        int protocol,
        IntPtr source,
        IntPtr target,
        int uid,
        IntPtr output,
        int outputLength)
    {
        var sourceText = Marshal.PtrToStringUTF8(source) ?? string.Empty;
        var targetText = Marshal.PtrToStringUTF8(target) ?? string.Empty;
        var process = GetCallbacks(context).ResolveProcess(protocol, sourceText, targetText, uid);
        return WriteUtf8(process, output, outputLength);
    }

    private static IAndroidTunCallbacks GetCallbacks(IntPtr context)
    {
        return (IAndroidTunCallbacks)GCHandle.FromIntPtr(context).Target!;
    }

    private static int WriteUtf8(string value, IntPtr output, int outputLength)
    {
        if (output == IntPtr.Zero || outputLength <= 0)
        {
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var length = Math.Min(bytes.Length, outputLength - 1);
        Marshal.Copy(bytes, 0, output, length);
        Marshal.WriteByte(output, length, 0);
        return length;
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

    private static string LastError()
    {
        var value = NativeLastError();
        return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;
    }

    private static void ThrowIfFalse(int value, string message)
    {
        if (value == 0)
        {
            var detail = LastError();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}");
        }
    }
}
