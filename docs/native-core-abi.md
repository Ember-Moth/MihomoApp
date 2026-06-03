# Native Core ABI

Android loads a CGO `c-shared` library named `libclash.so` through .NET
P/Invoke. Put ABI-specific builds here:

```text
Mihomo.Android/NativeLibraries/armeabi-v7a/libclash.so
Mihomo.Android/NativeLibraries/arm64-v8a/libclash.so
Mihomo.Android/NativeLibraries/x86_64/libclash.so
```

The Android C# binding lives in `Mihomo.Android/Interop/LibClashNative.cs`.

iOS links a CGO `c-archive` static library and calls it through `__Internal`
P/Invoke. GitHub Actions downloads the artifact into:

```text
Mihomo.iOS.PacketTunnel/NativeLibraries/ios/iphoneos-arm64/libclash.a
Mihomo.iOS.PacketTunnel/NativeLibraries/ios/iphonesimulator/libclash.a
Mihomo.iOS.PacketTunnel/NativeLibraries/ios/include/libclash.h
Mihomo.iOS.PacketTunnel/NativeLibraries/ios/libclash.xcframework
```

The iOS C# binding lives in
`Mihomo.iOS.PacketTunnel/Interop/LibClashNative.cs`.

On iOS, `libclash_start_tun` is called from the Packet Tunnel extension, not the
main app. The extension obtains the TUN fd by scanning open file descriptors for
Darwin `utun` sockets with `getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME,
...)`. This mirrors common iOS 15+ packet tunnel integrations where
`NEPacketTunnelFlow` does not publicly expose the fd needed by Go TUN stacks.

`libclash_force_gc` is intended for memory pressure control in the iOS Packet
Tunnel extension. It asks the Go runtime to force GC and return free memory to
the OS where possible.

The expected C ABI is intentionally small and does not require JNI glue:

```c
typedef int (*protect_callback)(void *context, int fd);
typedef int (*query_socket_uid_callback)(
    void *context,
    int protocol,
    const char *source,
    const char *target);

void libclash_set_android_callbacks(
    void *context,
    protect_callback protect,
    query_socket_uid_callback query_socket_uid);

int libclash_init(const char *home_dir, int android_sdk_version);
char *libclash_validate_config(const char *config_path);
char *libclash_setup_config(const char *setup_json);
int libclash_start_listener(void);
int libclash_stop_listener(void);
int libclash_start_tun(
    int fd,
    const char *stack,
    const char *address_csv,
    const char *dns_csv);
void libclash_stop_tun(void);
void libclash_force_gc(void);
void libclash_update_dns(const char *dns_csv);
char *libclash_last_error(void);
void libclash_free_string(char *value);
```

String results returned by `libclash_validate_config` and
`libclash_setup_config` must be allocated by the native core and released by
`libclash_free_string`. `libclash_last_error` follows the same ownership rule.

`query_socket_uid_callback` returns the Android UID that owns the socket. On
Android 10+ the client should call `ConnectivityManager.GetConnectionOwnerUid`.
For older Android versions, the Go core falls back to `/proc` lookup and may not
call this callback.
