# Claude 接手说明

请先阅读 `AGENTS.md`。本文件只补充 Claude 或其他长上下文 agent 在本仓库工作时的执行约束。

## 工作方式

- 使用中文回复，保持结论清晰、可执行。
- 先读代码和文档，再做判断；不要凭历史上下文猜项目状态。
- 遇到用户要求“继续”时，先查看最近的 `git status --short` 和相关文件，不要重做已完成任务。
- 多 agent 协作时，按不重叠写入范围分工；主线程负责整合、构建和最终说明。
- 不要为了“完整”扩大范围。每轮只处理用户当前目标。

## 必读背景

项目目标不是 FlClash 复刻。Aureline 已转向自有 UI/UX：

- 视觉语言：曜线。
- 一级页面：总览、策略、配置、工具。
- UI P0-P4 第一轮已完成。
- 下一阶段是验收、细节打磨、Release/iOS 验证。

如果用户要求继续 UI，请优先查看：

```text
docs/ui-ux-design.md
docs/ui-ux-implementation-plan.md
Aureline/Views/MainView.axaml
Aureline/Views/Pages/*.axaml
Aureline/ViewModels/Main/*.cs
```

## 强约束

- 不要复刻 FlClash。
- 不要新增静态占位功能。
- 不要让共享 UI 直接调用 native P/Invoke。
- 不要在 iOS 主 App 运行 native core。
- 不要在 iOS 常规控制面启用 REST fallback。
- 不要恢复 `advanced/rules/scripts` 工具页占位。
- 不要把 Android、clash-rs、meow、iOS 的 ABI 强行统一。
- 不要自动提交，除非用户明确要求“提交”。

## 文件归属速查

```text
Aureline/Views/Pages/OverviewPageView.axaml   总览
Aureline/Views/Pages/ProxiesPageView.axaml    策略
Aureline/Views/Pages/ConfigPageView.axaml     配置
Aureline/Views/Pages/ToolsPageView.axaml      工具

Aureline/ViewModels/Main/MainViewModel.Config.cs   配置和订阅
Aureline/ViewModels/Main/MainViewModel.Proxies.cs  策略、节点、流量
Aureline/ViewModels/Main/MainViewModel.Runtime.cs  启停和 profile
Aureline/ViewModels/Main/MainViewModel.State.cs    SQLite 状态
Aureline/ViewModels/Main/MainViewModel.Tools.cs    工具页

Aureline/Services/Clash/CoreCapabilities.cs 或 ClashRuntimeModels.cs
  native core 能力模型，按实际文件为准。
```

## 构建验证

默认验证：

```bash
./scripts/dotnet11.sh build Aureline.Android/Aureline.Android.csproj \
  -c Debug \
  -f net11.0-android \
  -r android-arm64 \
  -v:minimal
```

Release/NativeAOT 验证：

```bash
./scripts/publish-android-nativeaot.sh android-arm64
```

如果构建失败，先修本轮引入的问题。不要用删除功能或回滚别人改动来绕过失败。

## 回复格式

最终回复要短，包含：

- 改了哪些文件/模块。
- 验证命令和结果。
- 未完成或风险点。

不要输出长篇过程日志。用户看不到工具输出，需要你转述关键结果。
