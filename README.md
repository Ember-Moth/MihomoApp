# Mihomo

Mihomo 是一个使用 Avalonia 12 和 .NET for Android/iOS 编写的
mihomo/clash 客户端壳工程。Go 核心不嵌入本仓库，原生核心由独立的
`Ember-Moth/libclash` 项目构建，本项目只通过 P/Invoke 消费
`libclash.so` / `libclash.a`。

```text
Avalonia UI / C#
        |
Android VpnService 或 iOS PacketTunnel NetworkExtension
        |
P/Invoke
        |
CGO libclash
        |
mihomo
```

本项目不使用 Kotlin-Dart、gomobile 绑定层或项目自有 C++ JNI 胶水。Android
系统能力通过 .NET for Android 绑定调用；iOS VPN 逻辑运行在独立的 Packet
Tunnel 扩展进程中。

## 设计边界

- `Mihomo/`：平台无关的 Avalonia UI、ViewModel、mihomo REST API 客户端和状态。
- `Mihomo.Android/`：Android Activity、`VpnService`、TUN fd、socket protect
  回调、`libclash.so` P/Invoke。
- `Mihomo.iOS/`：iOS 主 App，负责 `NETunnelProviderManager`。
- `Mihomo.iOS.PacketTunnel/`：iOS NetworkExtension，负责设置 tunnel、扫描
  `utun` fd、调用 `libclash.a`。
- `docs/`：项目结构和 native ABI 说明。

更细的目录归属见 `docs/project-structure.md`，C ABI 见
`docs/native-core-abi.md`。

## Android 开发环境

Android 构建基线是项目内固定的 .NET 11 Preview 4 SDK 与 Android NativeAOT。
先安装项目本地 SDK，再安装最小 Android workload：

```bash
./scripts/install-dotnet11-sdk.sh
./scripts/install-dotnet11-android-minimal.sh
```

如果你已经手动把 `.NET 11 Preview 4` 放到了 `.dotnet/`，可以只运行第二个脚本。

日常 Debug 编译：

```bash
./scripts/dotnet11.sh build Mihomo.Android/Mihomo.Android.csproj \
  -c Debug \
  -f net11.0-android \
  -r android-arm64
```

Release APK 默认使用 NativeAOT：

```bash
./scripts/publish-android-nativeaot.sh
```

当前 .NET 11 Preview 4 的 Android NativeAOT runtime pack 只覆盖：

```text
android-arm64
android-x64
```

因此 release 脚本默认产出这两个 RID 的 APK。只构建单个 RID：

```bash
./scripts/publish-android-nativeaot.sh android-arm64
```

Release 产物位置：

```text
Mihomo.Android/bin/Release/net11.0-android/android-arm64/publish/com.embermoth.mihomo-Signed.apk
Mihomo.Android/bin/Release/net11.0-android/android-x64/publish/com.embermoth.mihomo-Signed.apk
```

脚本会在发布前清理对应 RID 的 `bin/obj`，并检查 APK 中是否包含
`NativeAotRuntimeProvider`，避免 NativeAOT 增量构建产生进入即闪退的 APK。

## Android 原生核心

本仓库不提交 `libclash.so`。本地开发时，把 `libclash` 构建出的 so 放到：

```text
Mihomo.Android/NativeLibraries/arm64-v8a/libclash.so
Mihomo.Android/NativeLibraries/armeabi-v7a/libclash.so
Mihomo.Android/NativeLibraries/x86_64/libclash.so
```

Android GitHub Actions 会临时 checkout `Ember-Moth/libclash`，用 NDK 构建 stripped
c-shared so，再执行本项目的 NativeAOT release 发布。

## iOS 架构

iOS VPN 必须使用 NetworkExtension。主 App 不直接运行 mihomo 核心，只负责保存
和启动 `NETunnelProviderManager`；真正的 tunnel 逻辑在
`Mihomo.iOS.PacketTunnel` 扩展进程中。

PacketTunnel 扩展会设置 `NEPacketTunnelNetworkSettings`，随后扫描打开的 fd，
用 Darwin `getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME, ...)` 找到 `utun`
fd，再调用 `libclash_start_tun`。

iOS 的 `libclash.a` 由 `Ember-Moth/libclash` 的 GitHub Actions 产出。当前主仓库
的 iOS workflow 会下载 `libclash-ios` artifact，解包到：

```text
Mihomo.iOS.PacketTunnel/NativeLibraries/ios
```

本机不要求支持 iOS 构建，iOS 统一走 GitHub Actions。

## GitHub Actions

- `.github/workflows/android-release.yml`：构建 Android NativeAOT Release APK，
  上传 `mihomo-android-release` artifact。
- `.github/workflows/ios.yml`：在 `macos-26` runner 上构建 iOS simulator/device
  产物，并下载 `libclash-ios` artifact。

iOS workflow 固定使用 `macos-26`，因为 .NET 11 Preview 4 的 iOS workload 需要
Xcode 26 系列工具链；`macos-latest` 可能仍落到 Xcode 16.x，导致 workload 与
Xcode 版本不匹配。

## 运行时说明

Android：

- `VpnService` 创建 TUN interface 并把 fd 传给 `libclash_start_tun`。
- socket protect 和 UID 查询通过 reverse P/Invoke 回调给 Go。
- UI 只通过 `IClashRuntime` 操作运行时，不直接调用 `LibClashNative`。

iOS：

- `PacketTunnelProvider` 在扩展进程中链接 `libclash.a`。
- 扩展启动核心后和停止核心后会触发 .NET GC，并调用 `libclash_force_gc` 请求
  Go runtime 归还可释放内存。

## 许可

本项目使用 GPL-3.0 许可证，见 `LICENSE`。
