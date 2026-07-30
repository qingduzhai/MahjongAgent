# 12 WPF 落地说明

> 对应提示词 v2 第十节与交付项 11。本文件是"设计 → 第二阶段(ResourceDictionary + 纯 UI XAML + Mock ViewModel)"的映射约定,**本轮不产出工程代码**。技术栈:C# / .NET 10 / WPF(ADR-0001)。
> **v1.1 修订**(按 `review-codex.md`):P1-3 悬浮层改为单一 `OverlayHostWindow`;P0-3 交互模式落地为穿透摘挂;工程落位按决定 #13 拆为 Presentation / UI.Windows / Platform.Windows;ViewModel 清单按决定 #14 扩充;确认 CommunityToolkit.Mvvm(决定 #7)与文案资源键(决定 #4)。

## 1. 工程落位(review 决定 #13)

```text
MahjongAgent.Presentation(新增,跨平台,无 WPF/Win32 引用)
  只读 ViewModel、UI 数据契约、Command 抽象、文案资源键

MahjongAgent.UI.Windows(新增,WPF)
  Views(XAML)、ResourceDictionary、TileView 等自定义控件
  └── Themes/
      ├── Colors.xaml         ← 02 号文件 §2 全部 Brush.* Token
      ├── Typography.xaml     ← §3 Font.* + 字号层级 Style
      ├── Metrics.xaml        ← §4 Space.*/Radius.*/Shadow.*
      ├── Tiles.xaml          ← §5 三档尺寸 + TileView 模板与状态触发器
      ├── Motion.xaml         ← §6 Duration/Easing
      └── Controls.xaml       ← 11 号文件组件模板

MahjongAgent.Platform.Windows(收窄)
  应用宿主、窗口捕获、悬浮窗 Win32 互操作(穿透/热键/跟随)、托盘、密钥
  —— 只负责宿主和原生能力,不放 UI 样式与 ViewModel
```

Token → XAML 映射示例(示意,非交付代码):

```xml
<SolidColorBrush x:Key="Brush.Brand.Default" Color="#4BA88F"/>
<CornerRadius x:Key="Radius.Card">8</CornerRadius>
<Style x:Key="Text.Body" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="Microsoft YaHei UI"/>
    <Setter Property="FontSize" Value="13"/>
</Style>
```

文案:首版仅中文,但**全部文案使用资源键**(如 `Text.Overlay.Monitoring`),不写硬编码字符串,为未来 i18n 留接口(决定 #4)。

## 2. 控件映射表

| 设计元素 | WPF 结构 |
|---|---|
| 数字牌桌 | 640×640 固定设计面 `Grid`(3×3 方位区)放入 `Viewbox` 等比缩放;弃河/副露 `ItemsControl`;缩放 <0.75 时外层 `ScrollViewer` 接管(P1-4) |
| 麻将牌 | `TileView : ContentControl`,依赖属性:`Tile`、`Size(S/M/L)`、`State`、`Mark(癞/皮/杠)`;**控件不含规则逻辑**,可打出与否等由 ViewModel 数据驱动(P0-1);以 34 种 canonical 牌面做可辨识性视觉测试(决定 #6) |
| 悬浮三层 | **单一 `OverlayHostWindow`**(P1-3):`ContentControl` + 按状态切换 `ContentTemplate`(胶囊/紧凑卡/详细面板);统一管理 Topmost 顺序、窗口跟随、DPI、穿透切换、展开动画、生命周期与互斥;仅当原型证明单窗口无法满足时再拆分 |
| 卡片/芯片/状态条 | `Border` + `ContentControl`;状态用 `DataTrigger`,不写 code-behind 逻辑 |
| 置信度趋势/危险度图 | 轻量自绘 `Polyline` 与 `ProgressBar` 重模板,不引第三方图表库 |
| 事件流/建议历史/Episode 列表 | `ItemsControl` + `VirtualizingStackPanel` |
| 复盘时间线 | 横向 `ItemsControl` 节点 + `Canvas` 连接线(节点为 `Button` 重模板) |

## 3. 悬浮窗 Win32 要点(P0-3 落地,封装在 Platform.Windows)

- 常态样式:`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`,`Topmost`,`ShowActivated=False`;
- **模式切换 = 摘挂 `WS_EX_TRANSPARENT`**:PassiveOverlay 加样式(指针穿透);InteractiveOverlay 摘样式并允许获得焦点;退出交互(超时/Esc/再按热键/窗口丢失)后重新挂样式;
- 全局快捷键:`RegisterHotKey`(组合全部可配置,默认 `Alt+M`/`Alt+P` 仅为 Mock 占位);**不使用低级键盘钩子**,无"按住修饰键"方案;
- 跟随目标窗口:订阅窗口矩形变化,按 WindowProfile 锚点(优先窗口外侧,回退标定安全区,决定 #2)重算;Per-Monitor V2 DPI 感知;
- 托盘:NotifyIcon;首次关闭主窗口气泡提示一次。

## 4. MVVM 与边界(handoff-contract 约束)

- 使用 **CommunityToolkit.Mvvm**(Source Generators,决定 #7);
- 全部界面绑定**只读 Mock ViewModel**(位于 Presentation):
  `MainDashboard` / `CurrentGame` / `OverlayCapsule` / `CompactAdvice` / `StrategyPanel` / `WindowSelection` / `Calibration` / `CorrectionDialog` / `Profiles` / `Replay` / `Rules` / `Settings`
  **新增(决定 #14)**:`MemoryHealth` / `MemorySearch` / `MemoryReview` / `RetrievalAudit` / `OpponentInsight`;
- 交互动作一律 `ICommand`(暂停/恢复、重新同步、纠错、追问、跳转巡、记忆审核…),Mock 实现只改 Mock 状态;追问 ViewModel 只读 GameState/DecisionSnapshot/检索结果;
- **不写**:真实捕获、SQLite/密钥访问、真实 API、任何规则计算;XAML code-behind 仅初始化与纯视觉动画;
- Freezable 默认冻结;动画全部 `Storyboard`,禁用布局动画。

## 5. 第二阶段验收对齐(与 review §6 最小垂直切片一致)

1. Colors/Typography/Metrics ResourceDictionary 无硬编码色值/字号;
2. 34 种 TileView 及状态矩阵(可辨识性测试截图);
3. 单一 `OverlayHostWindow` + Passive/Interactive 两模式 + 胶囊/紧凑卡/详细面板切换;
4. Mock GameState、DecisionSnapshot、OpponentInsight;
5. 主窗口导航(七项,含记忆与学习)与仪表盘骨架;记忆与学习页线框;
6. 每页面标注最低尺寸,并以**真实布局验证**(非声明)覆盖:1024×640、1280×800、125%/150% DPI(P1-4);
7. 视觉回归只使用 Mock 牌桌,**不将真实游戏平台画面提交仓库**(决定 #15);
8. XAML 静态检查通过后,由 Codex 集成正式 ViewModel。

第二阶段不接入真实捕获、SQLite、API 或规则计算。
