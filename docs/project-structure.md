# 项目结构设计

这个仓库承载 Avalonia Android/iOS 客户端。mihomo 原生核心由相邻的
`/root/mihomo/libclash` 仓库独立构建，客户端只消费 ABI 产物
`libclash.so` / `libclash.a`，不把 Go 工程嵌入到 .NET 项目里。

## 工作区边界

```text
/root/mihomo/libclash
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
  Services/                  NETunnelProviderManager 控制逻辑

Mihomo.iOS.PacketTunnel/
  PacketTunnelProvider.cs    NEPacketTunnelProvider 扩展入口
  Interop/                   libclash.a P/Invoke 绑定
  Services/                  Packet tunnel runtime、utun fd 扫描、内存修剪
  NativeLibraries/ios        GitHub Actions 下载的 libclash iOS 产物
```

iOS 的 `libclash.a` 链接在 `Mihomo.iOS.PacketTunnel` 扩展进程内，P/Invoke
使用 `__Internal`。本机不构建 iOS，`Mihomo.iOS.PacketTunnel/NativeLibraries/ios`
只保留 `.gitkeep`，实际 native archive 由 GitHub Actions 下载到
`Mihomo.iOS.PacketTunnel/NativeLibraries/ios` 并传给扩展项目的 `NativeReference`。

主 App 不直接调用 `libclash`，只通过 `NETunnelProviderManager` 保存
`NETunnelProviderProtocol` 并启动 Packet Tunnel。扩展进程设置
`NEPacketTunnelNetworkSettings` 后扫描 0..1024 的 fd，用 Darwin
`getsockopt(fd, SYSPROTO_CONTROL, UTUN_OPT_IFNAME, ...)` 找到 `utun` fd，再调用
`libclash_start_tun`。

PacketTunnel 扩展 Release 配置启用 NativeAOT。内存控制由扩展进程自己做：
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
- 新全局颜色或控件样式：放到 `Styles/Palette.axaml` 或 `Styles/Controls.axaml`。
