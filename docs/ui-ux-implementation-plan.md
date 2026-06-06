# UI/UX 改造实施计划

本文把 `docs/ui-ux-design.md` 拆成可执行任务。当前目标不是一次性重写所有页面，
而是先建立 Aureline 自有 UI 基础，再逐步替换旧页面结构和占位功能。

## 并行工作流

### 设计系统

写入范围：

```text
Aureline/Styles/Palette.axaml
Aureline/Styles/Controls.axaml
```

任务：

- 替换粉紫 Material 风格 token，建立曜线语义色板。
- 补齐深色和浅色模式的 `Background`、`Surface`、`Stroke`、`Accent`、`Route`、
  `Success`、`Warning`、`Danger`、`ChartUpload`、`ChartDownload`。
- 收敛基础控件圆角，避免大面积 pill 化。
- 统一 nav、section、list row、segmented、icon button、status badge、bottom sheet
  等基础样式。

验收：

- 页面仍能编译加载。
- 旧资源 key 尽量保留兼容，避免一次性破坏所有 XAML。
- 新页面可以只依赖语义 token，不需要写死颜色。

### 导航壳

写入范围：

```text
Aureline/Views/MainView.axaml
Aureline/Views/MainView.axaml.cs
Aureline/ViewModels/Main/MainViewModel.cs
Aureline/ViewModels/Main/MainViewModel.State.cs
Aureline/ViewModels/Main/ToolItems.cs
```

任务：

- 把一级页面命名切换到“总览 / 策略 / 配置 / 工具”。
- 修复底部导航 item 居中和安全区域。
- 策略页显示规则改为由运行状态和 capability 共同决定。
- 建立统一返回行为：弹层、搜索、工具子页、页面栈、退出。
- 顶部 app bar 降低视觉重量，只放必要状态和操作。

验收：

- Android 返回键不直接退出，先处理当前 UI 层级。
- 未启动时不会进入不可用策略页，或进入时显示明确空状态。
- 顶部图标尺寸稳定，不挤压标题。

### 能力模型

写入范围：

```text
Aureline/Services/Clash/*
Aureline/ViewModels/Main/MainViewModel.Runtime.cs
Aureline/ViewModels/Main/MainViewModel.State.cs
```

任务：

- 新增 `CoreCapabilities`。
- `IClashRuntime` 暴露当前 native core 能力。
- 共享 ViewModel 根据 capability 派生 `CanShow...`、`CanUse...` 属性。
- 对 Go/mihomo、clash-rs、meow、iOS runtime 分别填充能力。

验收：

- 不支持的功能不再显示成可点击占位。
- 仍保留基本启动、停止、配置导入能力。
- meow 和 clash-rs 变体不会因为缺少某个高级 ABI 而让 UI 状态异常。

### 页面重构

写入范围：

```text
Aureline/Views/Pages/OverviewPageView.axaml
Aureline/Views/Pages/ProxiesPageView.axaml
Aureline/Views/Pages/ConfigPageView.axaml
Aureline/Views/Pages/ToolsPageView.axaml
Aureline/ViewModels/Main/MainViewModel.*.cs
Aureline/ViewModels/Main/ProfileItems.cs
Aureline/ViewModels/Main/ProxyItems.cs
Aureline/Views/Controls/*
```

任务：

- 总览页：真实运行状态、60 秒流量曲线、网络检测、内网地址、最近事件。
- 策略页：出站模式、策略组索引、整行可点节点、测速状态。
- 配置页：只保留 profile/订阅管理，详情页处理启用、更新、校验、删除。
- 工具页：拆成应用、核心、数据、诊断四组；子页面不再使用通用保存模板。

验收：

- 所有一级页面都有空、加载、错误、运行中状态。
- 静态占位按钮清零。
- 长文本、深浅色、移动端触摸区域通过真机检查。

## 串行依赖

1. 设计系统先落地，页面重构才能统一使用新 token。
2. 能力模型先落地，页面按钮才能正确显示或禁用。
3. 导航壳先落地，工具子页和配置详情才能进入统一返回栈。
4. 页面重构按“总览 -> 策略 -> 配置 -> 工具”的顺序推进。

## 首轮任务清单

### P0：基础框架

- [x] 改造 `Palette.axaml` 的深浅色语义 token。
- [x] 收敛 `Controls.axaml` 中过度 pill 化的按钮和卡片。
- [x] 新增 `CoreCapabilities` 并接入 `IClashRuntime`。
- [x] 统一导航命名为“总览 / 策略 / 配置 / 工具”。
- [x] 修复底部导航 item 居中和策略页显示规则。

### P1：总览页

- [x] 把启动状态区从 floating FAB 转为页面核心区域。
- [x] 流量图改为最近 60 秒窗口。
- [x] 网络检测区显示成功、失败、未检测三种状态。
- [x] 内网地址从运行时状态获取；暂时拿不到时显示“未分配”。
- [x] 加入最近事件列表。

### P2：策略页

- [x] 出站模式由 runtime/capability 驱动，不写死为 Rule/Global/Direct。
- [x] 策略组切换使用自有策略组轨道。
- [x] 节点 item 改为整行可点，统一 padding/margin。
- [x] 单节点测速和当前组测速按 capability 显示。

### P3：配置页

- [x] Profile item 点击进入详情，不直接模拟复杂菜单。
- [x] URL 导入固定使用 `clash` UA。
- [x] 二维码入口在能力未接入前禁用并说明原因。
- [x] 删除/更新/校验进入明确的命令状态。

### P4：工具页

- [x] 根页按“应用 / 核心 / 数据 / 诊断”重新分组，替换当前“设置 / 其他”结构。
- [x] 清理或实装 `advanced`、`rules`、`scripts`：当前 `advanced` 根入口没有 ViewModel 路由，`rules` 和 `scripts` 子页只是说明型占位。
- [x] 主题、语言、备份恢复、应用设置保留真实业务子页面；没有平台能力的入口必须隐藏、禁用或给出明确原因。
- [x] Geo 低内存模式根据 `CoreCapabilities.SupportsGeodataMemoryMode` 显示。
- [x] 工具页未发现不适用的“保存 / 校验 / 重载”通用按钮；旧 `ProfilesPageView` 仍有同类按钮，但不属于当前工具页 P4 范围。

P4 审计记录：

- `advanced`、`rules`、`scripts` 入口和子页已移除，工具页不再展示无业务占位。
- 访问控制、系统代理、DNS 劫持、外部控制器、Geo 低内存模式已接入 capability 显示规则。
- 隐藏返回按钮、WebDAV 文案、恢复策略静态文本、Hosts 说明占位已清理。
- 应用设置页只保留当前能生效的标签页动画和自动关闭连接。

## 子 agent 分工

当前并行分工：

```text
设计系统 agent：Palette.axaml / Controls.axaml
导航壳 agent：MainView / MainViewModel 导航相关
能力模型 agent：Services/Clash / runtime state
页面审计 agent：只读审计四个页面和 ViewModel
```

主线程负责：

- 维护任务边界。
- 整合返回补丁。
- 解决交叉依赖。
- 运行构建和真机验证。

## 验证命令

Debug 编译：

```bash
./scripts/dotnet11.sh build Aureline.Android/Aureline.Android.csproj \
  -c Debug \
  -f net11.0-android \
  -r android-arm64
```

Release NativeAOT：

```bash
./scripts/publish-android-nativeaot.sh android-arm64
```

UI 变更完成后至少检查：

- Android 真机启动不闪退。
- 底部导航居中。
- 返回键行为正确。
- 深色/浅色切换正确。
- 启动/停止、订阅导入、策略切换仍可用。
