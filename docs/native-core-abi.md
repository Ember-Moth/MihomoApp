# Native Core ABI

The default Android app loads a CGO `c-shared` library named `libclash.so`
through .NET P/Invoke. Put ABI-specific builds here:

```text
Aureline.Android/NativeLibraries/armeabi-v7a/libclash.so
Aureline.Android/NativeLibraries/arm64-v8a/libclash.so
Aureline.Android/NativeLibraries/x86_64/libclash.so
```

The Android C# binding lives in `Aureline.Android/Interop/LibClashNative.cs`.

There are also Android core variants:

```text
Aureline.ClashRs.Android/NativeLibraries/armeabi-v7a/libclash.so
Aureline.ClashRs.Android/NativeLibraries/arm64-v8a/libclash.so
Aureline.ClashRs.Android/NativeLibraries/x86_64/libclash.so
Aureline.ClashRs.Android/NativeLibraries/*/libboring_noise-*.so
Aureline.ClashRs.Android/NativeLibraries/*/libtun_rs-*.so

Aureline.Meow.Android/NativeLibraries/armeabi-v7a/libmeow.so
Aureline.Meow.Android/NativeLibraries/arm64-v8a/libmeow.so
Aureline.Meow.Android/NativeLibraries/x86_64/libmeow.so
```

`Aureline.ClashRs.Android` currently reuses the default Android binding because
its mobile FFI exports `libclash_*` symbols. `Aureline.Meow.Android` has its own
`Interop/LibClashNative.cs`; the C# class name stays the same only so the shared
Android runtime can be linked unchanged. The native function names are
`libmeow_*`, and that ABI is not required to match iOS or the Go `libclash`
ABI exactly.

Android 16 will require 16 KB native library page sizes. Rust Android cores
should be linked with a 16 KB max page size, for example:

```bash
-C link-arg=-Wl,-z,max-page-size=16384
```

iOS links a CGO `c-archive` static library and calls it through `__Internal`
P/Invoke. GitHub Actions downloads the artifact into:

```text
Aureline.iOS.PacketTunnel/NativeLibraries/ios/iphoneos-arm64/libclash.a
Aureline.iOS.PacketTunnel/NativeLibraries/ios/iphonesimulator/libclash.a
Aureline.iOS.PacketTunnel/NativeLibraries/ios/include/libclash.h
Aureline.iOS.PacketTunnel/NativeLibraries/ios/libclash.xcframework
```

The iOS C# binding lives in
`Aureline.iOS.PacketTunnel/Interop/LibClashNative.cs`.

On iOS, `libclash_start_tun` is called from the Packet Tunnel extension, not the
main app. The extension obtains the TUN fd by scanning open file descriptors for
Darwin `utun` sockets with `getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME,
...)`. This mirrors common iOS 15+ packet tunnel integrations where
`NEPacketTunnelFlow` does not publicly expose the fd needed by Go TUN stacks.

The iOS main app does not call `libclash` and does not use mihomo
`external-controller` for normal UI control. It sends compact JSON messages to
the Packet Tunnel extension with `NETunnelProviderSession.SendProviderMessage`.
The extension handles those messages and calls the ABI below in-process.

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
void libclash_reset(void);
long long libclash_query_traffic_now(void);
long long libclash_query_traffic_total(void);
int libclash_query_connection_count(void);
void libclash_close_all_connections(void);
int libclash_set_mode(const char *mode);
char *libclash_query_group_names(int exclude_not_selectable);
char *libclash_query_group(const char *name, const char *sort_mode);
int libclash_patch_selector(const char *selector, const char *name);
void libclash_health_check(const char *name);
void libclash_health_check_all(void);
int libclash_test_proxy_delay(
    const char *name,
    const char *test_url,
    int timeout_milliseconds);
int libclash_start_tun(
    int fd,
    const char *stack,
    const char *address_csv,
    const char *dns_csv);
void libclash_stop_tun(void);
void libclash_force_gc(void);
long long libclash_set_memory_limit(long long bytes);
int libclash_set_gc_percent(int percent);
void libclash_update_dns(const char *dns_csv);
char *libclash_last_error(void);
void libclash_free_string(char *value);
```

String results returned by `libclash_validate_config`, `libclash_setup_config`,
`libclash_query_group_names`, `libclash_query_group`, and
`libclash_last_error` must be allocated by the native core and released by
`libclash_free_string`.

`query_socket_uid_callback` returns the Android UID that owns the socket. On
Android 10+ the client should call `ConnectivityManager.GetConnectionOwnerUid`.
For older Android versions, the Go core falls back to `/proc` lookup and may not
call this callback.

On iOS Packet Tunnel, call `libclash_set_memory_limit` and
`libclash_set_gc_percent` before loading a profile. They map to Go runtime memory
controls and are meant to keep the native core below the tighter NetworkExtension
memory budget. They are soft controls; the extension still runs periodic
best-effort .NET and Go memory trimming.

## iOS PacketTunnel IPC

The main app sends one JSON object per request. The current request shape is:

```json
{
  "action": "query-group",
  "groupName": "Proxy",
  "proxyName": "HK-01",
  "mode": "rule",
  "testUrl": "https://www.gstatic.com/generate_204",
  "timeoutMilliseconds": 5000,
  "configPath": "/path/to/config.yaml",
  "sortMode": "Default"
}
```

Supported actions:

- `status`
- `validate-config`
- `query-group-names`
- `query-group`
- `traffic`
- `connection-count`
- `select-proxy`
- `set-mode`
- `test-proxy-delay`
- `health-check`
- `close-connections`
- `force-gc`

The response is also JSON:

```json
{
  "ok": true,
  "error": "",
  "payload": "",
  "longValue": 0,
  "secondLongValue": 0,
  "intValue": 0,
  "boolValue": false
}
```

`payload` carries native JSON returned by `libclash_query_group_names` and
`libclash_query_group`. `longValue` and `secondLongValue` carry packed traffic
snapshots for upload/download rate and totals.
