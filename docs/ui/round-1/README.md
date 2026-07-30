# 第一轮 UI 设计交付:MahjongAgent / 麻将观察助手

- 交付方:Kimi(第一阶段:视觉方向、设计 Token、页面信息层级、高保真稿、组件状态与交互说明)
- 依据:[Kimi UI 设计提示词 v2](../kimi-ui-prompt-v2.md)、[ADR-0002 产品界面职责](../../adr/0002-product-surfaces.md)、[总体设计方案](../../architecture.md)、[Agent 与长期记忆后端设计方案](../../backend-agent-memory.md)
- 本轮范围:**只交设计**。不含后端、窗口捕获、数据库、真实 AI 调用,不生成完整工程或成品 XAML(见 [UI 协作边界](../handoff-contract.md))。
- 审核方式:请对照 [UI 审核清单](../ui-review-checklist.md) 与 `13-open-questions.md` 逐项审核。

## 修订记录

- **v1.0**(2026-07-30):首轮 13 项交付。
- **v1.1**(2026-07-30):按 [Codex 审核意见](review-codex.md) 完成全部 P0/P1 修订——
  P0-1 规则示例全部限定 RuleProfile 作用域(牌集 34 种 136 张为全局常量;七对/癞子处置等按参考规则修正;实测只晋升"本档案已确认");
  P0-2 删除"推荐 94%"式伪概率,改为推荐等级 + 相对差异;
  P0-3 悬浮层交互重做为 PassiveOverlay / InteractiveOverlay 双模式,删除"按住 Alt";
  P0-4 新增"记忆与学习"一级页面(14 号文件)并接入导航、仪表盘与详细策略面板;
  P1-1~P1-8 逐项修订;第一轮 15 个开放问题全部按裁决落地(13 号文件 §A)。
- **v1.2**(2026-07-30):按 [第二轮审核](review-codex-round-2.md) 完成——
  R2-P0-1 InteractiveOverlay 同时摘除 TRANSPARENT 与 NOACTIVATE、显式激活、记录并恢复前台窗口("不抢焦点"仅限 Passive);
  R2-P0-2 Provider 配置覆盖 OpenAI-Compatible 参数(预设/Base URL/Path/模型 ID/认证/结构化输出/图片 detail),测试连接展示已验证能力与失败分类,换 Key 事务式;
  R2-P1-1 "点击面板外"以 `Deactivated` 判定,不用覆盖游戏的透明命中层,交互超时在输入法组合/追问发送/纠错弹窗期间暂停;
  R2-P1-2 记忆晋升硬性门禁(验证未过禁用+原因;确认=人工复核,晋升由后端状态机执行并记录版本),回滚明确为发布操作;
  R2-P1-3 推荐层级改动作语义(首选/备选/不建议),牌效差值解释排序,`较低/中/较高` 只用于风险;
  第二轮 5 个问题全部按裁决落地(13 号文件 §B);第二阶段准入顺序见 12 号文件 §5。
- **v1.3**(2026-07-30):按第三轮复审结论完成 3 个必须修正项——
  ① Provider 地址契约对齐后端:Base URL 必须以 `/` 结尾、Completions Path 不允许 `/` 开头(08 §1②、10 §二、12 §4);
  ② 「自定义 Header」补齐 Header 名称/Key 前缀/附加 Header 输入(08 §1②、10 §二、12 §4);
  ③ 「自动探测」改为 UI 探测动作,持久化仅 `JsonSchema`/`JsonObject`(08 §1②、10 §二、12 §4);
  两条 WPF 实现约束写入 12 §3 并同步 03/04/05/11:`Deactivated` 自有窗口/输入法候选窗作用域过滤(纠错弹窗/文件选择器/输入法激活时不退出);前台恢复分路径(仅 Esc/热键/超时/显式关闭恢复原游戏窗口,`Deactivated` 路径不恢复);
  三个开放问题产品方直接定案:能力探测存 `LastProbe` 且配置变化即失效、离线预设追问首版内置文案资源、允许 JsonSchema→JsonObject 降级(合成图探测+本地校验,显示「结构化输出降级」)(13 号文件 §C)。
- **第三轮审核结论**:整体设计通过,两个 P0 已关闭;3 个必须修正项、2 条 WPF 约束、3 个定案已全部落地,当前无阻塞设计的开放问题。
- **第二轮审核**：[Codex 对 UI v1.1 的审核](review-codex-round-2.md)。主要问题已关闭；InteractiveOverlay 激活样式与 OpenAI-Compatible Provider 配置需在对应 XAML 前修订。

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
| — | 记忆与学习(review P0-4 新增页) | [14-memory-and-learning.md](14-memory-and-learning.md) |

## 统一示例数据(Mock)

全套稿件共用同一局 Mock 牌局,便于审核时交叉比对:

- 目标窗口:`微信 - 小程序`(进程 `WeChat.exe`);
- WindowProfile:`武汉麻将·默认皮肤 A`(`wechat-wuhan-a`),版本 v3,匹配度 96%;
- 规则档案(示例,值仅属本档案):`红中赖子杠 · 口口翻`(`wuhan-koukoufan` v3);牌集为标准 34 种 136 张(万筒条 1–9 + 东南西北中发白,无花牌);
- 牌局:`game-20260730-001`,第 6 巡,轮到自家;
- 翻牌五筒 → 癞子六筒,皮四筒/五筒(认定规则:本档案已确认/待确认,见 10 号文件);红中杠已出 1 次(自家,按本档案规则);
- 四家:自家开口×1(碰八筒);下家开口×2(碰白板、吃 2·3·4 条,危险度较高·推测);对家未开口;上家开口×1(碰西风,危险度中·推测);
- 自家手牌 11+摸 1(有副露者手牌为 11 张):二三四五六万 · 三筒 四筒(皮) 六筒(癞) 九筒(低置信度 61%) · 三条 白板 + 摸入九条;
- 建议:首选打九条(动作语义:首选/备选/不建议,无等级无概率),一向听,有效进张 12 张,相比备选进张 +4;识别可信度 96%;
- API:Mock Provider,本局识别调用 17 次,估算成本 ¥0.34;延迟链:画面稳定→事件 1.4s,事件→建议 0.3s。

> 牌面与数值均为设计占位数据,不构成规则断言。规则值全部以 RuleProfile 示例呈现并带确认状态(本档案已确认/待确认/来源冲突),未确定项不写死结论(见 10 号文件)。

## 尺寸清单(提示词要求的最小集合)

- 主窗口:1280×800(最低 1024×640);
- 待机胶囊:180×40;
- 紧凑建议卡:360×160(含警示条变体 360×184);
- 详细策略面板:420×520(标题/底栏固定,中部滚动)。
