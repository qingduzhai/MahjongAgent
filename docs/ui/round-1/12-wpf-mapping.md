# 12 WPF 落地说明

> 对应提示词 v2 第十节与交付项 11。本文件是"设计 → 第二阶段(ResourceDictionary + 纯 UI XAML + Mock ViewModel)"的映射约定,**本轮不产出工程代码**。技术栈:C# / .NET 10 / WPF(ADR-0001)。

## 1. 资源字典结构(第二阶段按此拆分)

```text
MahjongAgent.Platform.Windows(或独立 UI 项目,待确认,见 13 号文件)
└── Themes/
    ├── Colors.xaml         ← 02 号文件 §2 全部 Brush.* Token
    ├── Typography.xaml     ← §3 Font.* + 字号层级 Style(TextBlock)
    ├── Metrics.xaml        ← §4 Space.*/Radius.*/Shadow.*(Thickness、CornerRadius、DropShadowEffect)
    ├── Tiles.xaml          ← §5 三档尺寸 + TileView 控件模板与 10 状态触发器
    ├── Motion.xaml         ← §6 Duration/Easing 资源
    └── Controls.xaml       ← 11 号文件组件模板:Btn/Input/Card/Chip/NavItem/Row/Dialog/Banner
```

Token → XAML 映射示例(示意,非交付代码):

```xml
<!-- Colors.xaml 中的一行示例 -->
<SolidColorBrush x:Key="Brush.Brand.Default" Color="#4BA88F"/>
<!-- Metrics.xaml -->
<CornerRadius x:Key="Radius.Card">8</CornerRadius>
<!-- Typography.xaml -->
<Style x:Key="Text.Body" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="Microsoft YaHei UI"/>
    <Setter Property="FontSize" Value="13"/>
</Style>
```

## 2. 控件映射表

| 设计元素 | WPF 结构 |
|---|---|
| 数字牌桌 | `Grid`(3×3 方位区)+ 每方位 `ItemsControl`(弃河 Tile.S,WrapPanel)+ `ItemsControl`(副露 Tile.M)+ 中心 `Border` 信息块;不绘自绘 Canvas 自由图形 |
| 麻将牌 | 自定义 `TileView : ContentControl`,依赖属性:`Tile`、`Size(S/M/L)`、`State(普通/选中/推荐/危险/低置信度/不可选)`、`Mark(癞/皮/杠)`;模板内 `Border` 层叠角标 `TextBlock` |
| 悬浮三层 | 三个独立 `Window`(AllowsTransparency=True、WindowStyle=None、Topmost=True、ShowActivated=False、Background=Transparent,根 `Border` 承载 `Brush.Bg.Overlay`) |
| 状态条/卡片/芯片 | `Border` + `ContentControl`;状态用 `DataTrigger` 切换,不写 code-behind 逻辑 |
| 置信度趋势/危险度图 | 轻量自绘 `Polyline`(趋势)与 `ProgressBar` 重模板(危险度条),不引第三方图表库 |
| 事件流/建议历史 | `ItemsControl` + `VirtualizingStackPanel`(长列表虚拟化) |
| 时间线(复盘) | `ItemsControl` 横向节点 + `Canvas` 连接线(只读,节点为 `Button` 重模板) |

## 3. 悬浮窗 Win32 要点(平台外壳内,UI 层不感知)

- 扩展样式:`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`;鼠标穿透态加 `WS_EX_TRANSPARENT`(可交互态摘除);
- 跟随目标窗口:轮询/事件订阅目标窗口矩形变化,按 WindowProfile 锚点重算位置;Per-Monitor V2 DPI 感知(`WM_DPICHANGED` 重算);
- 全局快捷键:`RegisterHotKey`(`Alt+M`/`Alt+P`);按住 `Alt` 检测用低级键盘钩子的轻量方案或 `GetAsyncKeyState` 轮询(实现细节第二阶段定);
- 托盘:NotifyIcon;首次关闭主窗口时气泡提示一次。

## 4. MVVM 与边界(handoff-contract 约束)

- 全部界面绑定**只读 Mock ViewModel**:`MainDashboardViewModel` / `CurrentGameViewModel` / `OverlayCapsuleViewModel` / `CompactAdviceViewModel` / `StrategyPanelViewModel` / `WindowSelectionViewModel` / `CalibrationViewModel` / `CorrectionDialogViewModel` / `ProfilesViewModel` / `ReplayViewModel` / `RulesViewModel` / `SettingsViewModel`;
- 交互动作一律 `ICommand`(暂停/恢复、重新同步、纠错、跳转巡…),Mock 实现只改 Mock 状态;
- **不写**:真实捕获、SQLite/密钥访问、真实 API、任何规则计算;XAML code-behind 仅保留初始化与纯视觉动画;
- 建议 MVVM 工具:CommunityToolkit.Mvvm(Source Generators),与 .NET 10 兼容——是否引入待确认(13 号文件 Q7);
- Freezable 资源(Brush 等)统一 `x:Shared=False` 之外默认冻结;动画全部走 `Storyboard`,禁用布局动画。

## 5. 第二阶段验收对齐(与审核清单"工程交付"对应)

1. ResourceDictionary 无硬编码色值/字号,全部引用 Token;
2. 每页面标注最低尺寸并在 1024×640 截图验证;
3. 每组件提供六态(默认/悬停/按下/禁用/加载/错误)的视觉回归截图;
4. 牌面素材为原创矢量 `TileView`,无版权风险资产;
5. 悬浮层在不激活、穿透、DPI 125%/150% 下截图验证;
6. XAML 静态检查通过后,由 Codex 集成正式 ViewModel。
