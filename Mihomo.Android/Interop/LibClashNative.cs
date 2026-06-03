using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mihomo.Android.Interop;

internal interface IAndroidTunCallbacks
{
    bool Protect(int fd);

    int QuerySocketUid(int protocol, string source, string target);
}

internal static unsafe partial class LibClashNative
{
    private const string Library = "libclash.so";

    private static GCHandle? callbackHandle;

    private static delegate* unmanaged[Cdecl]<IntPtr, int, int> ProtectThunk => &HandleProtect;

    private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, int> QuerySocketUidThunk =>
        &HandleQuerySocketUid;

    [LibraryImport(Library, EntryPoint = "libclash_set_android_callbacks")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeSetAndroidCallbacks(
        IntPtr context,
        delegate* unmanaged[Cdecl]<IntPtr, int, int> protect,
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, int> querySocketUid);

    [LibraryImport(Library, EntryPoint = "libclash_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeInit(
        string homeDir,
        int androidSdkVersion);

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

    [LibraryImport(Library, EntryPoint = "libclash_free_string")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeFreeString(IntPtr value);

    [LibraryImport(Library, EntryPoint = "libclash_last_error")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeLastError();

    public static void SetAndroidCallbacks(IAndroidTunCallbacks callbacks)
    {
        if (callbackHandle is { IsAllocated: true } handle)
        {
            handle.Free();
        }

        callbackHandle = GCHandle.Alloc(callbacks);
        NativeSetAndroidCallbacks(GCHandle.ToIntPtr(callbackHandle.Value), ProtectThunk, QuerySocketUidThunk);
    }

    public static void Init(string homeDir, int androidSdkVersion)
    {
        ThrowIfFalse(NativeInit(homeDir, androidSdkVersion), "libclash_init failed");
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

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandleProtect(IntPtr context, int fd)
    {
        return GetCallbacks(context).Protect(fd) ? 1 : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandleQuerySocketUid(
        IntPtr context,
        int protocol,
        IntPtr source,
        IntPtr target)
    {
        var sourceText = Marshal.PtrToStringUTF8(source) ?? string.Empty;
        var targetText = Marshal.PtrToStringUTF8(target) ?? string.Empty;
        return GetCallbacks(context).QuerySocketUid(protocol, sourceText, targetText);
    }

    private static IAndroidTunCallbacks GetCallbacks(IntPtr context)
    {
        return (IAndroidTunCallbacks)GCHandle.FromIntPtr(context).Target!;
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
