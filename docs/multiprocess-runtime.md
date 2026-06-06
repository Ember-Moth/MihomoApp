# 多进程运行时架构

Aureline 的运行时采用统一的控制面/数据面分离模型：

```text
共享 UI / ViewModel
  -> IClashRuntime
    -> 平台 IPC 客户端
      -> VPN/Core 进程
        -> P/Invoke native core
        -> TUN / utun fd
```

核心原则：

- UI 进程不直接加载或调用代理核心。
- 代理核心只在 VPN/Core 进程内初始化、加载配置、启动 listener、接管 TUN。
- 策略组、流量、连接数、节点切换、测速、健康检查等控制能力都通过 IPC 暴露。
- 不把 `external-controller` 作为应用内默认控制面；它只作为调试或特殊场景开关。

## Android

Android 的主 App 进程只运行 Avalonia UI、订阅/配置管理、数据库和权限入口。
真正的 core 运行在同一个 APK 的私有进程：

```text
com.embermoth.aureline:vpn
```

三个 Android 变体都使用相同结构：

```text
Aureline.Android          -> com.embermoth.aureline:vpn
Aureline.ClashRs.Android  -> com.embermoth.aureline.clashrs:vpn
Aureline.Meow.Android     -> com.embermoth.aureline.meow:vpn
```

`:vpn` 进程内有两个 service：

- `AurelineCoreService`：持有 native core 状态，处理 IPC 命令。
- `AurelineVpnService`：创建 Android VPN interface，拿到 TUN fd，并处理
  `protect`、UID 查询和前台通知栏网速。

UI 进程中的 `AndroidClashRuntime` 只负责：

- 请求 Android VPN 权限。
- 通过 `AndroidCoreIpcClient` 发送 `Messenger + JSON` 控制消息。
- 把 IPC 响应转换为共享层模型。
- 查询已安装应用列表，这个能力仍然属于 UI/设置侧。

`AurelineCoreService` 负责：

- `libclash/libmeow` 初始化和配置加载。
- 启停 listener 和 TUN。
- 查询策略组、流量、连接数。
- 切换节点、切换出站模式、测速、健康检查、关闭连接。

Android IPC command 与 iOS PacketTunnel action 应保持语义一致。当前 Android 命令：

```text
initialize
validate-config
start
stop
get-status
get-proxy-groups
get-traffic
get-connection-count
select-proxy
set-mode
test-proxy-delay
health-check
health-check-all
close-all-connections
```

## iOS

iOS 天然是多进程结构：

```text
Aureline.iOS 主 App
  -> NETunnelProviderSession.SendProviderMessage
    -> Aureline.iOS.PacketTunnel NetworkExtension
      -> P/Invoke libclash.a
      -> 扫描 utun fd
```

主 App 不直接链接或调用 native core。PacketTunnel 扩展负责配置
`NEPacketTunnelNetworkSettings`、扫描 `utun` fd、启动 core，并通过
`SendProviderMessage` 响应控制消息。

## 统一方向

Android 和 iOS 的底层 IPC 机制不同，但共享层应只看到同一套能力：

```text
IClashRuntime
  InitializeAsync
  ValidateConfigAsync
  StartAsync
  StopAsync
  GetProxyGroupsAsync
  GetTrafficAsync
  GetConnectionCountAsync
  SelectProxyAsync
  SetModeAsync
  TestProxyDelayAsync
  HealthCheckAsync
  CloseAllConnectionsAsync
```

后续新增控制能力时，优先按这个顺序落地：

1. 在共享层模型中定义语义。
2. 扩展 Android `AndroidIpcCommands` 和 `AurelineCoreService`。
3. 扩展 iOS PacketTunnel action 和 `PacketTunnelRuntime`。
4. ViewModel 只调用 `IClashRuntime`，不关心平台 IPC 细节。

不要让某个平台重新回退到 REST API 或直接 P/Invoke，否则 UI 会再次和 core 状态
耦合，Android/iOS 行为也会分叉。
