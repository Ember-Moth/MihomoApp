# 项目结构设计

这个仓库承载 Avalonia Android/iOS 客户端。mihomo 原生核心通过 `libclash/`
git submodule 挂载，仍作为独立 Go Module 构建。客户端只消费 ABI 产物
`libclash.so` / `libclash.a`，不在 .NET 项目内混写 Go 代码。

## 工作区边界

```text
/root/mihomo/Mihomo/libclash
  Go + CGO native core
  输出 Android libclash.so 和 iOS libclash.a/XCFramework

/root/mihomo/Mihomo
  Avalonia 12 + .NET for Android/iOS 客户端
  通过 P/Invoke 调用 libclash native library
```

## 共享项目

```text
Mihomo/
  App.axaml(.cs)             应用入口和全局资源挂载
  Assets/                    Avalonia 资源
  Models/                    平台无关数据模型
  Services/Clash/            mihomo API 客户端和运行时抽象
  Styles/                    全局色板和控件样式
  ViewModels/                Avalonia ViewModel
  Views/                     Avalonia 视图
```

`Models/` 只放稳定的数据结构，例如 `ClashProfile`、`ClashStatus`。

`Services/Clash/` 放共享层服务：

- `IClashRuntime`：客户端调用原生核心的运行时接口。
- `ClashRuntimeHost`：平台运行时注册点，Android 启动时安装具体实现。
- `ClashApiClient`：访问 mihomo `external-controller` REST API。

`ViewModels/Main/` 放主界面的 partial ViewModel。当前主界面还处在快速成型
阶段，用 partial 按职责拆开：

- `MainViewModel.cs`：共享状态、导航和通用执行包装。
- `MainViewModel.Runtime.cs`：启动、停止、Profile 组装。
- `MainViewModel.Config.cs`：配置文件、订阅导入、校验。
- `MainViewModel.Proxies.cs`：策略组、节点切换、流量统计。
- `ProxyItems.cs`：策略组和节点列表项模型。

`Views/` 保持页面拆分：

```text
Views/
  MainView.axaml             顶部状态栏、页面容器、底部导航
  Pages/
    OverviewPageView.axaml   概览和流量
    ProfilesPageView.axaml   订阅导入和配置入口
    ProxiesPageView.axaml    策略组和节点切换
    ConfigPageView.axaml     运行参数和 config.yaml 编辑
```

`Styles/` 不放业务 UI：

- `Palette.axaml`：语义颜色资源。
- `Controls.axaml`：按钮、输入框、卡片等通用控件样式。

## Android 项目

```text
Mihomo.Android/
  MainActivity.cs            Avalonia Activity 和 Android 权限入口
  Application.cs             Android Application 初始化
  Interop/                   libclash.so P/Invoke 绑定
  Services/                  Android 平台运行时实现
  Vpn/                       VpnService、TUN fd、Android socket callbacks
  NativeLibraries/           ABI 分目录的 libclash.so
  Resources/                 Android 原生资源
```

`Interop/LibClashNative.cs` 是唯一的 P/Invoke 边界。不要在 UI 或 ViewModel
里直接调用它。

`Services/AndroidClashRuntime.cs` 实现共享层 `IClashRuntime`，负责初始化
libclash、写入 setup JSON、启动或停止 Android VPN 服务。

`Vpn/ClashVpnService.cs` 只处理 Android VPN 生命周期、TUN fd 和
`protect/query uid` 回调。它不负责 UI 状态。

## iOS 项目

```text
Mihomo.iOS/
  AppDelegate.cs             Avalonia iOS 启动入口
  Program.cs                 UIKit Main
  Info.plist                 iOS bundle 元数据
  Services/                  NETunnelProviderManager 和 PacketTunnel IPC

Mihomo.iOS.PacketTunnel/
  PacketTunnelProvider.cs    NEPacketTunnelProvider 扩展入口
  Interop/                   libclash.a P/Invoke 绑定
  Services/                  Packet tunnel runtime、控制 IPC、utun fd 扫描、内存修剪
  NativeLibraries/ios        GitHub Actions 下载的 libclash iOS 产物
```

iOS 的 `libclash.a` 链接在 `Mihomo.iOS.PacketTunnel` 扩展进程内，P/Invoke
使用 `__Internal`。`Mihomo.iOS.PacketTunnel/NativeLibraries/ios` 只保留
`.gitkeep`，实际 native archive 由 GitHub Actions 从 `libclash/` submodule
构建到该目录，并传给扩展项目的 `NativeReference`。

主 App 不直接调用 `libclash`，只通过 `NETunnelProviderManager` 保存
`NETunnelProviderProtocol` 并启动 Packet Tunnel。启动后，主 App 通过
`NETunnelProviderSession.SendProviderMessage` 向扩展发送查询策略组、切换节点、
查询流量、测速等控制消息。扩展进程设置 `NEPacketTunnelNetworkSettings` 后扫描
0..1024 的 fd，用 Darwin
`getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME, ...)` 找到 `utun` fd，再调用
`libclash_start_tun`。后续控制消息也由扩展进程内的
`Interop/LibClashNative.cs` 直接 P/Invoke `libclash.a`。

iOS 默认不启用 mihomo `external-controller`。原因是主 App 与 PacketTunnel 是
两个进程，HTTP listener 会增加额外控制面和内存占用；项目内 UI 所需能力由
PacketTunnel IPC 暴露。

Android Release APK 走 .NET 11 NativeAOT；PacketTunnel 扩展 Release 配置也启用
NativeAOT。内存控制由扩展进程自己做：
`MemoryPressure.Trim()` 会先触发 .NET GC/LOH compact，再调用
`libclash_force_gc` 触发 Go runtime `debug.FreeOSMemory()`。

## 依赖方向

```text
Views
  -> ViewModels
      -> Services/Clash
          -> Models

Mihomo.Android/Services
  -> Services/Clash + Models + Interop + Vpn

Mihomo.Android/Vpn
  -> Interop + Models

Mihomo.iOS/Services
  -> Services/Clash + Models + NetworkExtension

Mihomo.iOS.PacketTunnel
  -> Interop + NetworkExtension
```

共享项目不能引用 `Mihomo.Android` 或 `Mihomo.iOS`。平台项目可以引用共享项目。

UI 不直接接触 `libclash.so`，也不直接操作 Android `VpnService`。这些行为都
通过 `IClashRuntime` 和 ViewModel 命令进入。

## 新代码归位规则

- 新页面：放到 `Views/Pages/`，页面状态优先拆到 `ViewModels/Main/` 或新的
  feature ViewModel 目录。
- 新 mihomo REST 接口：扩展 `Services/Clash/ClashApiClient.cs`，不要放进页面
  code-behind。
- 新平台能力：先抽共享接口，再在对应平台的 `Services/` 实现。
- 新 P/Invoke ABI：改对应平台的 `Interop/LibClashNative.cs` 和
  `docs/native-core-abi.md`。
- 新 iOS 核心控制能力：优先扩展 PacketTunnel IPC action，再由
  `Mihomo.iOS/Services/IosClashRuntime.cs` 转换成共享层模型；不要在 iOS 主 App
  里重新启用 REST fallback。
- 新全局颜色或控件样式：放到 `Styles/Palette.axaml` 或 `Styles/Controls.axaml`。
