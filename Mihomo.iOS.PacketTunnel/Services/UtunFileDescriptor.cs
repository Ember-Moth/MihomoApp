using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation;
using NetworkExtension;

namespace Mihomo.iOS.PacketTunnel.Services;

internal static unsafe partial class UtunFileDescriptor
{
    private const string SystemLibrary = "/usr/lib/libSystem.dylib";
    private const int SearchFdLimit = 1024;
    private const int IfNameSize = 16;
    private const int SysProtoControl = 2;
    private const int UtunOptIfName = 2;

    [LibraryImport(SystemLibrary, EntryPoint = "getsockopt", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int GetSocketOption(
        int socket,
        int level,
        int optionName,
        byte* optionValue,
        ref uint optionLength);

    public static int Find(NEPacketTunnelFlow packetFlow)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(15))
        {
            var scanned = FindByScanningOpenFileDescriptors();
            if (scanned >= 0)
            {
                return scanned;
            }
        }

        return FindByPacketFlowKeyPath(packetFlow);
    }

    private static int FindByScanningOpenFileDescriptors()
    {
        Span<byte> prefix = stackalloc byte[] { (byte)'u', (byte)'t', (byte)'u', (byte)'n' };
        var buffer = stackalloc byte[IfNameSize];

        for (var fd = 0; fd <= SearchFdLimit; fd++)
        {
            for (var i = 0; i < IfNameSize; i++)
            {
                buffer[i] = 0;
            }

            var length = (uint)IfNameSize;
            if (GetSocketOption(fd, SysProtoControl, UtunOptIfName, buffer, ref length) != 0)
            {
                continue;
            }

            var actualLength = Math.Min((int)length, IfNameSize);
            if (actualLength < prefix.Length)
            {
                continue;
            }

            var isUtun = true;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (buffer[i] != prefix[i])
                {
                    isUtun = false;
                    break;
                }
            }

            if (isUtun)
            {
                return fd;
            }
        }

        return -1;
    }

    private static int FindByPacketFlowKeyPath(NEPacketTunnelFlow packetFlow)
    {
        using var keyPath = new NSString("socket.fileDescriptor");
        return packetFlow.ValueForKeyPath(keyPath) is NSNumber number
            ? number.Int32Value
            : -1;
    }
}
