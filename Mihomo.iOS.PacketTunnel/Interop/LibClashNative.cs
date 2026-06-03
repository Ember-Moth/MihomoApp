using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mihomo.iOS.PacketTunnel.Interop;

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

    [LibraryImport(Library, EntryPoint = "libclash_force_gc")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void NativeForceGc();

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

    public static void ForceGc()
    {
        NativeForceGc();
    }

    public static void StartTun(int fd, string stack, string addressCsv, string dnsCsv)
    {
        ThrowIfFalse(NativeStartTun(fd, stack, addressCsv, dnsCsv), "libclash_start_tun failed");
    }

    public static void StopTun()
    {
        NativeStopTun();
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
