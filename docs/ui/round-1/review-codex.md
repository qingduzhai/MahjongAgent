# Codex 对第一轮 UI 设计的审核

> 审核状态：方向通过，修改后进入第二阶段
> 日期：2026-07-30
> 范围：`docs/ui/round-1/` 全部13项交付

## 1. 总体结论

第一轮稿件完整、结构清晰，与“双界面产品形态”基本一致。视觉 Token、悬浮三层、异常状态、主窗口运维、复盘、隐私和 WPF 映射均达到可以继续深化的水平。

暂不建议直接进入完整 XAML，原因是存在四类需要先修订的问题：

1. Mock 规则中混入了错误或未验证的武汉麻将变体；
2. 推荐百分比容易被误解为胜率或可靠概率；
3. 悬浮层的鼠标穿透、点击、键盘和焦点模型互相冲突；
4. 新确定的“Agent记忆与自学习”核心尚未进入主窗口信息架构。

修订这些问题后，可以进入 ResourceDictionary、TileView 和 Mock ViewModel 阶段。

## 2. 已通过的方向

- 主窗口是配置、运维、诊断和完整牌局可视化；
- 悬浮助手是局内核心建议界面；
- 胶囊、紧凑卡、详细面板三级层次合理；
- 普通对手动作低干扰，高风险事件才主动展开；
- 明确区分确定事实、规则计算、启发式推测和低置信度观察；
- 状态覆盖较完整，没有只画正常态；
- 复盘使用事件溯源和 DecisionSnapshot，不设计成视频播放器；
- 视觉样本、API Key、诊断包和训练贡献边界清晰；
- WPF Token 化和 ResourceDictionary 拆分方向正确；
- 原创麻将牌视觉方案可以继续。

## 3. 阻塞第二阶段的问题

### P0-1 规则示例必须改为可配置，不得写死错误结论

涉及：`05-overlay-strategy-panel.md`、`10-rules-and-settings.md`、`11-components-states-flows.md`、`13-open-questions.md`。

问题：

- 标准牌集应为万、筒、条及东南西北中发白共34种、136张，武汉规则不含花牌；
- `13-open-questions.md` 的“万筒条+中发白”遗漏东南西北；
- `10-rules-and-settings.md` 把七对列为已验证大胡，但当前参考规则明确写有基础变体没有七对；
- 多处写死“癞子不可打出”，而参考规则中癞子可以打出或作癞子杠；
- “红中必杠”“发财/白板”“承包”等必须属于具体 RuleProfile，不能作为跨平台全局事实；
- 用户一次实测只能将规则标记为“本 WindowProfile 已确认”，不能自动晋升为全局已验证。

要求：所有 Mock 文案改为明确的 `RuleProfile` 示例，未确定项显示待确认，不在通用 TileView 或交互层写规则逻辑。

### P0-2 删除“推荐94%”式伪概率

涉及：`04-overlay-compact-card.md`、`05-overlay-strategy-panel.md`、`09-replay.md`。

问题：策略候选归一化分数不是胜率、正确率或置信概率。与识别96%并排会误导用户。

建议改为：

```text
首选
推荐等级：高
相比次选：有效进张 +4
识别可信度：96%
```

若未来有经过校准的胜率、放铳率或概率模型，再显示百分比，并注明模型、样本和误差范围。

### P0-3 重做悬浮层交互模式

涉及：`03-overlay-capsule.md`、`04-overlay-compact-card.md`、`05-overlay-strategy-panel.md`、`11-components-states-flows.md`、`12-wpf-mapping.md`。

当前设计同时要求：默认 `WS_EX_TRANSPARENT`、单击、右键、悬停、Tooltip、Esc、方向键、失焦自动收起。这些行为无法在同一非激活穿透状态下可靠共存。

建议正式拆成两种模式：

```text
PassiveOverlay
  不激活、鼠标穿透、只显示、仅响应全局热键

InteractiveOverlay
  由可配置全局热键进入
  临时移除穿透
  允许键盘、鼠标、追问和纠错
  超时或显式关闭后回 PassiveOverlay
```

删除“按住Alt进入交互态”的默认设计。Alt是系统和游戏常用修饰键，低级键盘钩子也不应成为首版依赖。

`Esc` 和方向键只在 InteractiveOverlay 获得焦点后可用。被动模式不能假设收到这些按键。

### P0-4 Agent长期记忆必须进入主窗口

涉及：`01-dual-surface-structure.md`、`06-main-dashboard.md`、`10-rules-and-settings.md`。

产品核心已经调整为策略准确性、长期记忆、检索与自学习。主窗口需要增加“记忆与学习”页面或一级区域：

- candidate / confirmed / rejected / superseded 数量；
- 最近一次后台整合；
- 记忆冲突和待审核项；
- 策略、规则、提示和模型版本；
- 相似牌局检索与命中理由；
- Retrieval Audit：候选、各分项得分和最终注入内容；
- 记忆来源、有效时间、适用 RuleProfile；
- 后台学习队列；
- 记忆体检、去重、回滚和删除。

详细策略面板需要能显示“参考了哪些历史经验”，但长期记忆管理仍留在主窗口。

## 4. 重要修订建议

### P1-1 低置信度不能统一给“倾向性建议”

如果不确定信息会改变合法动作、向听数、癞子身份或首选弃牌，则必须停止建议。只有不确定项经策略敏感性分析证明不影响首选时，才可以继续显示建议并标注影响范围。

```text
关键不确定 → 暂停建议
非关键不确定 → 保留建议 + 明确标注
```

### P1-2 详细策略面板需要 Agent 追问入口

产品已确认保留自然语言追问。`05-overlay-strategy-panel.md` 目前只有“为什么”抽屉，需在 InteractiveOverlay 中加入简短输入：

```text
问 Agent：为什么不打癞子？
```

追问只能读取当前 GameState、DecisionSnapshot 和已检索记忆，不修改牌局事实。

### P1-3 建议使用一个 OverlayHostWindow

`12-wpf-mapping.md` 建议三个独立 Window。首版优先使用一个 `OverlayHostWindow`，内部通过状态和 ContentTemplate 在胶囊、紧凑卡、详细面板间切换。这样更容易管理：

- Topmost顺序；
- 窗口跟随；
- DPI；
- 穿透切换；
- 展开动画；
- 生命周期与状态互斥。

只有技术原型证明一个窗口无法满足时再拆分。

### P1-4 修正固定尺寸的内容溢出

- `05`各分区标注高度总和已超过420×520，需要增加面板高度或明确中部滚动区；
- `04`低置信度态增加24px警示条后不能仍假定正常态160px布局；
- `07`的696px牌桌加页面标题可能超过704px内容高度；
- 需要对1024×640、125%和150% DPI做真实布局验证，而不是只写声明。

### P1-5 区分捕获频率和多模态识别频率

`06-main-dashboard.md` 的 `30fps` 容易让人误以为每帧都调用视觉模型。建议拆分为：

- 捕获采样率；
- 变化检测命中率；
- 稳定画面事件数；
- 多模态调用次数；
- 从画面稳定到 GameEvent 的延迟；
- 从 GameEvent 到建议的延迟。

### P1-6 修正隐私文案

“完整截图不保存”应明确表示“不持久化”。首次标定和异常恢复可能将经过遮罩的选中游戏窗口画面发送给用户配置的多模态 Provider。

应分别说明：

- 捕获；
- 上传；
- 本地短期缓冲；
- 本地长期保存；
- 匿名训练贡献。

### P1-7 离线时仍可保存本地档案候选

`08-onboarding-windows-profile.md` 写无网络时禁用“改进档案”。本地保存裁剪样本和 candidate 记忆不需要网络；只有再次调用云端模型时才需要网络。

### P1-8 数据保留策略需要一致

若历史牌局保留90天但原始事件仅30天，60天后的复盘无法重建。建议默认：

- 完整视觉证据：不持久化或短期可选；
- GameEvent、DecisionSnapshot、纠错：90天；
- 聚合统计和 confirmed memory：长期保留；
- 用户可一键删除。

## 5. 对 Kimi 开放问题的决定

| # | 决定 |
|---|---|
| 1 | 标准34种、136张：万筒条1–9，东南西北中发白，不含花牌。特殊杠与癞子由RuleProfile定义。 |
| 2 | 吸附位置进入WindowProfile；优先窗口外侧，屏幕不足时使用标定出的窗口内安全区。 |
| 3 | 接受“用户回合 + 高风险提醒”；普通对手动作只更新信息条。 |
| 4 | 首版中文，但所有文案使用资源键，为未来i18n留接口。 |
| 5 | 首版只做深色主窗口；悬浮层必须在深浅游戏背景上可读。 |
| 6 | 接受原创抽象矢量牌，但必须以34种canonical tile测试可辨识性。 |
| 7 | 接受CommunityToolkit.Mvvm。 |
| 8 | 接受Bahnschrift及系统回退字体。 |
| 9 | 6秒默认可保留；确认用户出牌后立即收起，10秒兜底。 |
| 10 | 改为敏感性判断：关键不确定项停止建议，非关键项才显示带范围的建议。 |
| 11 | 快捷键全部可配置；Alt+M/Alt+P仅作Mock，不使用“按住Alt交互”。 |
| 12 | GameEvent和DecisionSnapshot默认90天，长期confirmed memory不随牌局日志自动过期。 |
| 13 | 新增`MahjongAgent.Presentation`、`MahjongAgent.UI.Windows`，Platform.Windows只负责宿主和原生能力。 |
| 14 | 原12个ViewModel方向可用；新增MemoryHealth、MemorySearch、MemoryReview、RetrievalAudit、OpponentInsight。 |
| 15 | 视觉回归只使用Mock牌桌，不将真实游戏平台画面提交仓库。 |

## 6. 第二阶段建议交付范围

在完成P0修订后，第二阶段先交最小垂直切片：

1. Colors/Typography/Metrics ResourceDictionary；
2. 34种 TileView 及状态矩阵；
3. 单一 OverlayHostWindow；
4. Passive/Interactive 两种模式；
5. 胶囊、紧凑卡、详细面板切换；
6. Mock GameState、DecisionSnapshot、OpponentInsight；
7. 主窗口导航与仪表盘骨架；
8. 记忆与学习页面线框；
9. 1024×640、1280×800、125%/150% DPI视觉验证。

不在第二阶段接入真实捕获、SQLite、API或规则计算。
