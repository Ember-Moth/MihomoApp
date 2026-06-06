# Aureline

**Aureline**（曜线）是一个使用 Avalonia 12 和 .NET for Android/iOS 编写的代理客户端壳工程。
共享 UI 和业务状态放在 `Aureline/`，Android 侧拆成多个 native core 变体。默认
`Aureline.Android` 仍然使用 Go/mihomo `libclash`；另外提供 clash-rs 和 meow 两个
实验变体，复用同一套应用主体，但各自使用自己的 native library 和 P/Invoke ABI。

```text
Avalonia UI / C#
        |
IClashRuntime
        |
Android :vpn CoreService 或 iOS PacketTunnel NetworkExtension
        |
P/Invoke
        |
libclash / clash-rs / libmeow
        |
代理核心
```

本项目不使用 Kotlin-Dart、gomobile 绑定层或项目自有 C++ JNI 胶水。Android
系统能力通过 .NET for Android 绑定调用；Android core/VPN 逻辑运行在独立的
`:vpn` 进程中，iOS VPN 逻辑运行在独立的 Packet Tunnel 扩展进程中。

Aureline 不是 mihomo、Clash、MetaCubeX、clash-rs 或 meow 的官方项目，也不把
这些第三方 core 名称作为应用品牌使用。

## 设计边界

- `Aureline/`：平台无关的 Avalonia UI、ViewModel、mihomo REST API 客户端和状态。
- `Aureline.Android/`：默认 Android 变体，使用 Go/mihomo `libclash.so`。
- `Aureline.ClashRs.Android/`：clash-rs Android 变体，使用 clash-rs 产出的
  `libclash.so` 和它依赖的动态库。
- `Aureline.Meow.Android/`：meow Android 变体，使用 `libmeow.so` 和独立的
  `libmeow_*` P/Invoke ABI。
- `Aureline.iOS/`：iOS 主 App，负责 `NETunnelProviderManager`。
- `Aureline.iOS.PacketTunnel/`：iOS NetworkExtension，负责设置 tunnel、扫描
  `utun` fd、调用 `libclash.a`。
- `libclash/`：Go/CGO native core submodule，服务默认 Android 变体和当前 iOS。
- `docs/`：项目结构和 native ABI 说明。

更细的目录归属见 `docs/project-structure.md`，C ABI 见
`docs/native-core-abi.md`。自有 UI/UX 设计方案见 `docs/ui-ux-design.md`，
实施计划见 `docs/ui-ux-implementation-plan.md`。Android/iOS 统一多进程运行时
方案见 `docs/multiprocess-runtime.md`。

## Android 开发环境

Android 构建基线是项目内固定的 .NET 11 Preview 4 SDK 与 Android NativeAOT。
先初始化 submodule，再安装项目本地 SDK 和最小 Android workload：

```bash
git submodule update --init --recursive
./scripts/install-dotnet11-sdk.sh
./scripts/install-dotnet11-android-minimal.sh
```

如果你已经手动把 `.NET 11 Preview 4` 放到了 `.dotnet/`，可以只运行第二个脚本。

日常 Debug 编译：

```bash
./scripts/dotnet11.sh build Aureline.Android/Aureline.Android.csproj \
  -c Debug \
  -f net11.0-android \
  -r android-arm64
```

clash-rs 和 meow 变体的 Debug 编译：

```bash
./scripts/dotnet11.sh build Aureline.ClashRs.Android/Aureline.ClashRs.Android.csproj \
  -c Debug \
  -f net11.0-android \
  -r android-arm64

./scripts/dotnet11.sh build Aureline.Meow.Android/Aureline.Meow.Android.csproj \
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
Aureline.Android/bin/Release/net11.0-android/android-arm64/publish/com.embermoth.aureline-Signed.apk
Aureline.Android/bin/Release/net11.0-android/android-x64/publish/com.embermoth.aureline-Signed.apk
```

脚本会在发布前清理对应 RID 的 `bin/obj`，并检查 APK 中是否包含
`NativeAotRuntimeProvider`，避免 NativeAOT 增量构建产生进入即闪退的 APK。

## Android 原生核心

本仓库不提交各变体的 Android `.so`。本地开发时，默认 Go/mihomo 变体从
submodule 构建并安装 so：

```bash
ANDROID_NDK_HOME=/opt/android-sdk/ndk/27.1.12297006 \
INSTALL_DIR="$PWD/Aureline.Android/NativeLibraries" \
bash libclash/scripts/build-cshared-android.sh
```

安装位置：

```text
Aureline.Android/NativeLibraries/arm64-v8a/libclash.so
Aureline.Android/NativeLibraries/armeabi-v7a/libclash.so
Aureline.Android/NativeLibraries/x86_64/libclash.so
```

Android GitHub Actions 会初始化 `libclash/` submodule，用 NDK 构建 stripped
c-shared so，再执行本项目的 NativeAOT release 发布。

三个 Android 变体的包名不同，可以同时安装在真机上：

```text
Aureline.Android          com.embermoth.aureline
Aureline.ClashRs.Android  com.embermoth.aureline.clashrs
Aureline.Meow.Android     com.embermoth.aureline.meow
```

`Aureline.Android/Interop/LibClashNative.cs`、`Aureline.ClashRs.Android` 复用的
`libclash_*` ABI、以及 `Aureline.Meow.Android/Interop/LibClashNative.cs` 不要求
完全一致。共享应用主体只依赖 `IClashRuntime`，不同 core 的 ABI 差异应在各自
Android 项目的 interop 文件内适配。

## iOS 架构

iOS VPN 必须使用 NetworkExtension。主 App 不直接运行 mihomo 核心，只负责保存
和启动 `NETunnelProviderManager`；真正的 tunnel 逻辑在
`Aureline.iOS.PacketTunnel` 扩展进程中。

PacketTunnel 扩展会设置 `NEPacketTunnelNetworkSettings`，随后扫描打开的 fd，
用 Darwin `getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME, ...)` 找到 `utun`
fd，再调用 `libclash_start_tun`。

iOS 正常运行不启用 mihomo `external-controller`。主 App 和 PacketTunnel 扩展
之间使用系统提供的 `NETunnelProviderSession.SendProviderMessage` 做 IPC：

- 主 App 发送查询策略组、查询流量、切换节点、切换出站模式、测速、健康检查等
  小 JSON 消息。
- PacketTunnel 扩展在同一进程内 P/Invoke `libclash.a`，执行对应 C ABI。
- 扩展返回紧凑 JSON 响应，主 App 再转换成共享层 `ClashProxyGroup`、
  `ClashTraffic` 等 UI 模型。

这样控制面不会额外启动 HTTP listener，也不会把核心状态暴露到主 App 进程。
`external-controller` 只应作为 Android 或未来调试场景的可选能力，不作为 iOS
App/Extension 的默认通信方式。

iOS 的 `libclash.a` 在主仓库 iOS workflow 内从 `libclash/` submodule 构建，
输出到：

```text
Aureline.iOS.PacketTunnel/NativeLibraries/ios
```

本机不要求支持 iOS 构建，iOS 统一走 GitHub Actions。workflow 会产出未签名的
`ios-arm64` NativeAOT IPA，随后在本地用 Apple 账号对应的证书和描述文件重签。

下载最新成功 iOS workflow artifact：

```bash
./scripts/download-ios-artifact.sh
```

使用本地 `zsign` 重签：

```bash
P12=/path/to/apple-development-or-distribution.p12 \
P12_PASSWORD='p12 password' \
APP_PROVISION=/path/to/com.embermoth.aureline.mobileprovision \
EXTENSION_PROVISION=/path/to/com.embermoth.aureline.PacketTunnel.mobileprovision \
./scripts/sign-ios-zsign.sh
```

`APP_PROVISION` 和 `EXTENSION_PROVISION` 必须来自同一个 Apple Team，并分别匹配
主 App 与 PacketTunnel 扩展的 bundle id。两个 profile 都需要包含项目所需的
App Group；PacketTunnel 扩展 profile 还需要 NetworkExtension packet tunnel
能力。

## GitHub Actions

- `.github/workflows/android-release.yml`：构建 Android NativeAOT Release APK，
  上传 `aureline-android-release` artifact。
- `.github/workflows/ios.yml`：在 `macos-26` runner 上从 submodule 构建
  `libclash.a`，然后构建 unsigned iOS simulator App 和 unsigned `ios-arm64`
  NativeAOT IPA。该 IPA 只用于后续自行重签/侧载测试，不是可直接安装或分发的
  签名包。

iOS workflow 固定使用 `macos-26`，因为 .NET 11 Preview 4 的 iOS workload 需要
Xcode 26 系列工具链；`macos-latest` 可能仍落到 Xcode 16.x，导致 workload 与
Xcode 版本不匹配。

## 运行时说明

Android：

- UI 进程只通过 `IClashRuntime` 和 `Messenger + JSON` IPC 操作运行时。
- `AurelineCoreService` 运行在 `:vpn` 进程内，负责加载 native core、配置、listener、
  策略组、流量、节点切换和测速。
- `AurelineVpnService` 也运行在 `:vpn` 进程内，创建 TUN interface 并把 fd 传给
  native core；socket protect 和 UID 查询通过 reverse P/Invoke 回调处理。

iOS：

- `PacketTunnelProvider` 在扩展进程中链接 `libclash.a`。
- 主 App 通过 `SendProviderMessage` 请求扩展查询策略组、流量、连接数、测速和
  切换节点；这些操作都在扩展进程内直接调用 `libclash`。
- 扩展启动核心后和停止核心后会触发 .NET GC，并调用 `libclash_force_gc` 请求
  Go runtime 归还可释放内存。

## 许可

Aureline 自有客户端代码使用 Apache-2.0 许可证，见 `LICENSE`。

包含 Go/mihomo `libclash` 的 Android/iOS 发布产物需要同时满足 GPL-3.0 义务；
GPL-3.0 全文见 `LICENSES/GPL-3.0.txt`。clash-rs 变体还需要按其 `NOTICE.md`
判断启用 feature 后的许可证边界。第三方组件说明见 `NOTICE`。
