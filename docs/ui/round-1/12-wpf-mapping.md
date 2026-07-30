# 12 WPF 落地说明

> 对应提示词 v2 第十节与交付项 11。本文件是"设计 → 第二阶段(ResourceDictionary + 纯 UI XAML + Mock ViewModel)"的映射约定,**本轮不产出工程代码**。技术栈:C# / .NET 10 / WPF(ADR-0001)。
> **v1.1 修订**(按 `review-codex.md`):P1-3 单一 `OverlayHostWindow`;P0-3 交互模式;工程落位 Presentation / UI.Windows / Platform.Windows;ViewModel 扩充;CommunityToolkit.Mvvm 与文案资源键。
> **v1.2 修订**(按 `review-codex-round-2.md`):R2-P0-1 交互态窗口样式与激活/焦点恢复的状态转换;R2-P1-1 `Deactivated` 退出与计时暂停;R2-P0-2 Provider 配置模型进入设置页规格;准入顺序按第二轮审核 §6。
> **v1.3 修订**(第三轮审核):`Deactivated` 自有窗口/输入法候选窗作用域过滤;前台恢复分路径(仅 Esc/热键/超时/显式关闭恢复);Provider 配置模型对齐后端契约——BaseUrl 尾 `/` 必须、Path 不允许 `/` 开头、自定义 Header 名称/前缀/附加 Header、结构化输出持久化仅 JsonSchema/JsonObject(自动探测为 UI Command)、`LastProbe` 快照与失效。

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
  应用宿主、窗口捕获、悬浮窗 Win32 互操作(样式切换/激活/热键/跟随)、托盘、密钥
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
| 悬浮三层 | **单一 `OverlayHostWindow`**(P1-3):`ContentControl` + 按状态切换 `ContentTemplate`(胶囊/紧凑卡/详细面板);统一管理 Topmost 顺序、窗口跟随、DPI、穿透与激活切换、展开动画、生命周期与互斥;仅当原型证明单窗口无法满足时再拆分 |
| 卡片/芯片/状态条 | `Border` + `ContentControl`;状态用 `DataTrigger`,不写 code-behind 逻辑 |
| 置信度趋势/危险度图 | 轻量自绘 `Polyline` 与 `ProgressBar` 重模板,不引第三方图表库 |
| 事件流/建议历史/Episode 列表 | `ItemsControl` + `VirtualizingStackPanel` |
| 复盘时间线 | 横向 `ItemsControl` 节点 + `Canvas` 连接线(节点为 `Button` 重模板) |

## 3. 悬浮窗 Win32 要点(P0-3/R2 落地,封装在 Platform.Windows,UI 层不直接调用)

模式与窗口样式状态转换(R2-P0-1):

```text
PassiveOverlay(默认)
  WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
  ShowActivated=False
  不获取键盘焦点

进入 InteractiveOverlay(全局热键)
  保留 WS_EX_TOOLWINDOW
  摘除 WS_EX_TRANSPARENT 与 WS_EX_NOACTIVATE
  记录此前前台窗口 HWND
  显式激活 OverlayHostWindow,焦点放到选中控件(详细面板默认追问框)

退出 InteractiveOverlay
  清除输入焦点
  恢复 WS_EX_NOACTIVATE 与 WS_EX_TRANSPARENT
  前台恢复分路径:仅 Esc/热键/超时/显式关闭恢复此前前台窗口;
    Deactivated(点击其他应用)路径不恢复,避免抢回焦点(v1.3)
```

- "不抢焦点"只适用于 PassiveOverlay;InteractiveOverlay 是用户经全局热键显式请求的临时激活态;
- **退出判定**(R2-P1-1):Esc / 再按热键 / 显式关闭 / 窗口 `Deactivated`(焦点转到外部应用)/ 无操作超时(默认 30s,可配);**不使用**覆盖游戏窗口的透明命中层;OverlayHostWindow 保持内容大小;
- **`Deactivated` 作用域过滤(v1.3 约束)**:打开纠错弹窗、文件选择器或输入法候选窗口时,Overlay 同样会收到 `Deactivated`,**不得因此退出**。实现:收到 `Deactivated` 时判断新激活窗口是否属于「自有作用域」——本进程窗口树(纠错弹窗、由本进程弹出的文件选择器)或输入法候选窗(`GetForegroundWindow` + IMM 相关窗口类)——属于则忽略本次事件,闲置计时同时暂停;
- **前台窗口恢复分路径(v1.3 约束)**:**仅** Esc、热键、超时、显式关闭这四种退出路径恢复此前的游戏前台窗口;用户点击其他应用导致的 `Deactivated` 退出**不恢复**(恢复会把焦点从用户刚切过去的应用抢回来);
- **计时暂停**:输入法组合、追问请求发送中、纠错弹窗打开期间暂停无操作计时,弹窗关闭后恢复(决定 #1);
- 全局快捷键:`RegisterHotKey`(组合全部可配置,默认 `Alt+M`/`Alt+P` 仅为 Mock 占位);**不使用低级键盘钩子**;
- 跟随目标窗口:订阅窗口矩形变化,按 WindowProfile 锚点(优先窗口外侧,回退标定安全区,决定 #2)重算;Per-Monitor V2 DPI 感知;
- 托盘:NotifyIcon;首次关闭主窗口气泡提示一次。

## 4. MVVM 与边界(handoff-contract 约束)

- 使用 **CommunityToolkit.Mvvm**(Source Generators,决定 #7);
- 全部界面绑定**只读 Mock ViewModel**(位于 Presentation):
  `MainDashboard` / `CurrentGame` / `OverlayCapsule` / `CompactAdvice` / `StrategyPanel` / `WindowSelection` / `Calibration` / `CorrectionDialog` / `Profiles` / `Replay` / `Rules` / `Settings`
  **新增(决定 #14)**:`MemoryHealth` / `MemorySearch` / `MemoryReview` / `RetrievalAudit` / `OpponentInsight`;
- Provider 设置页 ViewModel 需承载 R2-P0-2 配置模型(v1.3 对齐后端契约):BaseUrl(**尾 `/` 必须**,否则拼接丢失 `v1` 段)、ChatCompletionsPath(**相对路径,`/` 开头即配置错误**)、模型 ID、认证(`ApiKeyTransport`:None/Bearer/Header;自定义 Header 含 `ApiKeyHeaderName`/`ApiKeyPrefix` 与 `AdditionalHeaders`)、超时/重试/Image Detail、非回环 http 需显式允许(`AllowInsecureHttp`);结构化输出持久化枚举仅 `JsonSchema`/`JsonObject`,**「自动探测」实现为 UI Command**——依次探测、成功后写入具体模式;仅 JsonObject 可用时走降级(合成图探测 + 本地 Schema/牌数/规则校验,界面显示「结构化输出降级」);能力探测结果持久化为 **`LastProbe`** 快照(含探测时间),打开设置页不重复探测,BaseUrl/Path/模型/认证/Key/输出模式任一变化即置失效并提示重新验证;`CredentialId` 内部生成不展示;
- 交互动作一律 `ICommand`(暂停/恢复、重新同步、纠错、追问、跳转巡、记忆审核、回滚发布…),Mock 实现只改 Mock 状态;追问 ViewModel 只读 GameState/DecisionSnapshot/检索结果;
- **不写**:真实捕获、SQLite/密钥访问、真实 API、任何规则计算;XAML code-behind 仅初始化与纯视觉动画;
- Freezable 默认冻结;动画全部 `Storyboard`,禁用布局动画。

## 5. 第二阶段准入顺序(第二轮审核 §6)

先做(设计侧已就绪):

1. ResourceDictionary(Colors/Typography/Metrics,无硬编码色值/字号);
2. 34 种 TileView 与状态矩阵(可辨识性测试截图);
3. 主窗口导航(七项,含记忆与学习)、仪表盘与记忆页静态骨架;
4. PassiveOverlay 纯展示模板(胶囊/紧凑卡/详细面板);
5. Mock ViewModel 与尺寸/DPI 回归:1024×640、1280×800、125%/150% 真实布局验证(P1-4)。

后做(待对应工程实现落地):

6. InteractiveOverlay 的输入与焦点(依赖 R2-P0-1 状态转换的工程实现);
7. Provider 设置页(依赖 R2-P0-2 配置模型)。

全程:视觉回归只使用 Mock 牌桌,**不将真实游戏平台画面提交仓库**(决定 #15);真实捕获、API、SQLite、规则计算不进入 Kimi 的 UI 切片;XAML 静态检查通过后由 Codex 集成正式 ViewModel。
