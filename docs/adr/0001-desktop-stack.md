# ADR-0001：桌面端技术栈

- 状态：Accepted
- 日期：2026-07-30

## 决策

采用纯 C#/.NET 10 构建跨平台领域核心，首个 Windows 外壳采用 WPF。Windows 捕获、悬浮层和窗口管理通过平台接口隔离。

## 原因

- 项目首发 Windows，深度依赖窗口捕获、透明悬浮层和 Win32；
- WPF 对置顶、鼠标穿透、不激活窗口和 DPI 处理更成熟；
- 规则、状态、Agent 和存储仍可保持跨平台；
- Electron 会引入 Chromium、Node.js 和原生桥接的双重复杂度；
- Avalonia 可以作为未来 Windows/macOS 共享 UI 的候选，但不消除平台捕获差异。

## 平台路线

- Windows：WPF + Windows Graphics Capture；
- macOS：未来使用 ScreenCaptureKit，并评估 Avalonia 或原生 AppKit 外壳；
- Android：受限的 MediaProjection + 悬浮窗版本；
- iOS：定位为复盘、上传分析或桌面 Companion，不承诺完整跨应用悬浮体验。

## 约束

- `MahjongAgent.Core` 不得引用任何 Windows 专属程序集；
- 平台捕获实现统一接入 `IFrameSource`；
- UI 不直接操作数据库或牌局状态；
- 多模态 Provider 不得直接写入正式事件存储。
