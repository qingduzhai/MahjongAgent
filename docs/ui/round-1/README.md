# 第一轮 UI 设计交付：MahjongAgent / 麻将观察助手

- 交付方：Kimi(第一阶段:视觉方向、设计 Token、页面信息层级、高保真稿、组件状态与交互说明)
- 依据:[Kimi UI 设计提示词 v2](../kimi-ui-prompt-v2.md)、[ADR-0002 产品界面职责](../../adr/0002-product-surfaces.md)、[总体设计方案](../../architecture.md)
- 本轮范围:**只交设计**。不含后端、窗口捕获、数据库、真实 AI 调用,不生成完整工程或成品 XAML(见 [UI 协作边界](../handoff-contract.md))。
- 审核方式:请对照 [UI 审核清单](../ui-review-checklist.md) 与本目录 `13-open-questions.md` 逐项审核。

## 文件索引(对应提示词第十一节交付顺序)

| # | 交付项 | 文件 |
|---|--------|------|
| 1 | 双界面产品结构说明 | [01-dual-surface-structure.md](01-dual-surface-structure.md) |
| 2 | 视觉方向和设计 Token | [02-visual-direction-tokens.md](02-visual-direction-tokens.md) |
| 3 | 待机胶囊高保真稿 | [03-overlay-capsule.md](03-overlay-capsule.md) |
| 4 | 紧凑建议卡正常与低置信度稿 | [04-overlay-compact-card.md](04-overlay-compact-card.md) |
| 5 | 详细策略面板稿 | [05-overlay-strategy-panel.md](05-overlay-strategy-panel.md) |
| 6 | 主窗口仪表盘稿 | [06-main-dashboard.md](06-main-dashboard.md) |
| 7 | 当前牌局完整可视化稿 | [07-game-visualization.md](07-game-visualization.md) |
| 8 | 首次启动、窗口选择、标定、纠错和窗口档案稿 | [08-onboarding-windows-profile.md](08-onboarding-windows-profile.md) |
| 9 | 牌局复盘稿 | [09-replay.md](09-replay.md) |
| — | 规则与知识、设置与诊断(页面规格) | [10-rules-and-settings.md](10-rules-and-settings.md) |
| 10 | 组件状态和交互流程 | [11-components-states-flows.md](11-components-states-flows.md) |
| 11 | WPF 落地说明 | [12-wpf-mapping.md](12-wpf-mapping.md) |
| 12 | 需要产品方确认的问题 | [13-open-questions.md](13-open-questions.md) |

## 统一示例数据(Mock)

全套稿件共用同一局 Mock 牌局,便于审核时交叉比对:

- 目标窗口:`微信 - 小程序`(进程 `WeChat.exe`);
- WindowProfile:`武汉麻将·默认皮肤 A`(`wechat-wuhan-a`),版本 v3,匹配度 96%;
- 规则档案:`红中赖子杠 · 七皮四癞 · 口口翻`;
- 牌局:`game-20260730-001`,第 6 巡,轮到自家;
- 翻牌五筒 → 癞子六筒,皮四筒/五筒;自家已开口 1 次(碰八筒),红中杠已出 1 次;
- 建议:打九条,一向听,有效进张 12 张,上家风险中;
- 识别置信度 96%,牌效为规则引擎确定性计算,风险评估为启发式推测(会明确标注);
- API:Mock Provider,最近延迟 1.2s,本局调用 17 次,估算成本 ¥0.34。

> 牌面与数值均为设计占位数据,不构成规则断言。武汉麻将牌张构成(是否含风牌、发财/白板是否作杠牌等)在各稿件中以"待确认规则"形式呈现,见 `10-rules-and-settings.md` 与 `13-open-questions.md`。

## 尺寸清单(提示词要求的最小集合)

- 主窗口:1280×800(最低 1024×640);
- 待机胶囊:180×40;
- 紧凑建议卡:360×160;
- 详细策略面板:420×520。
