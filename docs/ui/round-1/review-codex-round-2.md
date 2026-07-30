# Codex 对 UI v1.1 的第二轮审核

> 审核状态：主要问题已关闭，完成两项工程修订后进入 XAML 垂直切片
> 日期：2026-07-30
> 范围：`docs/ui/round-1/` v1.1 与新增 `14-memory-and-learning.md`

## 1. 结论

第二版已实质关闭上一轮四项 P0 和八项 P1：RuleProfile 作用域、伪概率、低置信度敏感性、单一 OverlayHostWindow、记忆与学习、追问、尺寸、指标、隐私、离线候选和保留期均已进入统一信息架构。

视觉 Token、TileView、主窗口静态骨架和 Mock ViewModel 可以进入 XAML。InteractiveOverlay 与 Provider 设置页需先修正以下两项 P0，避免把无法工作的交互和不完整的配置模型固化进控件。

## 2. 已通过

- 标准牌集固定为34种136张，变体规则全部进入 RuleProfile；
- 删除推荐百分比，识别可信度、规则计算、风险推测分开表达；
- 关键不确定时停止建议，非关键不确定展示影响范围；
- PassiveOverlay 与 InteractiveOverlay 的指针行为已分开；
- 采用单一 OverlayHostWindow 和 ContentTemplate 状态切换；
- 详细面板加入只读 Agent 追问和历史经验来源；
- 主窗口新增完整的记忆检索、审计、审核、Episode、队列和维护页面；
- 捕获采样、稳定画面、多模态调用和两段延迟不再混为一个指标；
- 捕获、上传、内存缓冲、长期保存和匿名贡献的隐私口径已拆分；
- GameEvent、DecisionSnapshot 与纠错统一保留90天，confirmed memory 长期保留；
- 离线仍可保存本局补偿事件、裁剪样本和本地 candidate；
- 牌桌固定设计面、面板滚动区和 DPI 验收范围已经明确；
- UI 工程边界拆为 Presentation、UI.Windows 和 Platform.Windows；
- 视觉回归只使用 Mock 牌桌。

## 3. 进入交互 XAML 前必须修订

### R2-P0-1 InteractiveOverlay 必须切换激活样式

涉及：`03-overlay-capsule.md`、`05-overlay-strategy-panel.md`、`11-components-states-flows.md`、`12-wpf-mapping.md`。

当前文档仍写窗口常驻 `WS_EX_NOACTIVATE`、`ShowActivated=False`，只在交互态摘掉 `WS_EX_TRANSPARENT`，同时又要求输入框、方向键和 Esc 获得键盘焦点。摘掉穿透只解决鼠标命中，不能让 `WS_EX_NOACTIVATE` 窗口获得焦点。

正式状态转换应为：

```text
PassiveOverlay
  WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
  ShowActivated=False
  不获取键盘焦点

InteractiveOverlay
  保留 WS_EX_TOOLWINDOW
  摘除 WS_EX_TRANSPARENT 与 WS_EX_NOACTIVATE
  记录此前前台窗口 HWND
  显式激活 OverlayHostWindow，并把焦点放到选中控件或追问框

退出 InteractiveOverlay
  清除输入焦点
  恢复 WS_EX_NOACTIVATE 与 WS_EX_TRANSPARENT
  尽力恢复此前前台窗口
```

“不抢焦点”只适用于 PassiveOverlay。InteractiveOverlay 是用户通过全局热键显式请求的交互状态，允许临时激活。

### R2-P0-2 Provider 配置必须覆盖 OpenAI-Compatible 参数

涉及：`08-onboarding-windows-profile.md`、`10-rules-and-settings.md`、`11-components-states-flows.md`。

当前只设计了 Provider 下拉框、模型和 API Key，无法配置已经落地的通用 Provider。至少需要：

```text
预设：OpenAI / 自定义兼容端点 / 本地端点
Base URL
Chat Completions Path（高级项）
视觉模型 ID（允许手工输入）
认证：Bearer / 自定义 Header / None
CredentialId（内部生成，不向普通用户展示）
结构化输出：自动探测 / JsonSchema / JsonObject
图片 detail：auto / low / high
```

“测试连接”必须展示已验证能力：鉴权、图片输入、JsonSchema 或 JsonObject、实际模型、耗时和请求 ID；失败区分配置、鉴权、限流、服务、超时、协议和能力不符。本地 `None` 认证时不要求 API Key。

更换 Key 应采用事务式体验：新 Key 测试成功后替换旧凭据；失败或取消时保留旧凭据。

## 4. 重要修订

### R2-P1-1 明确失焦和点击外部的实现

`点击面板外收起`不能依赖一个覆盖游戏窗口的透明命中层，否则会拦截游戏操作。OverlayHostWindow 保持内容大小；用户点击游戏导致窗口 `Deactivated` 时退出交互态。也可通过热键、Esc、显式关闭和超时退出。

输入法组合、追问请求发送和纠错弹窗打开期间暂停30秒无操作计时；纠错弹窗关闭后再恢复计时。

### R2-P1-2 记忆晋升操作需要硬性门禁

`14-memory-and-learning.md` 的“确认→confirmed”不能成为绕过验证的一键操作。规则验证、冲突处理和固定评测未通过时按钮必须禁用并显示原因；确认只表示人工复核通过，最终晋升仍由后端状态机执行并记录版本。

“版本与发布”不是只读页面：回滚属于发布操作，需二次确认、影响范围和审计记录。

### R2-P1-3 推荐层级改用动作语义

首版不再扩展“高/中/低/不推荐”四档。推荐动作固定使用：

```text
首选
备选
不建议
```

牌效差值负责解释排序；较低/中/较高只用于风险。这样避免“推荐等级低”到底是弱推荐还是不推荐的歧义。

## 5. 对第二轮开放问题的决定

| # | 决定 |
|---|---|
| 1 | InteractiveOverlay 默认30秒；输入框/输入法组合、请求处理中和纠错弹窗期间暂停计时。 |
| 2 | 首版追问只回答当前局面、规则和当前建议解释，不开放任意通用问答。无网络时保留确定性解释和预设问题，自由文本追问禁用。 |
| 3 | 仅检索审计、Episode、队列等高密度表格默认紧凑；页面导航、健康卡和审核详情保持舒适密度。 |
| 4 | 使用“首选/备选/不建议”三种动作语义，不增加第四个推荐强度档。 |
| 5 | 待审核 candidate 默认只显示导航徽标和主窗口摘要，不发托盘气泡；未来可增加显式开关。 |

## 6. 第二阶段准入

现在可以先实现：

1. ResourceDictionary；
2. 34种 TileView 与状态矩阵；
3. 主窗口导航、仪表盘和记忆页静态骨架；
4. PassiveOverlay 纯展示模板；
5. Mock ViewModel 与尺寸/DPI 回归。

完成 R2-P0-1 后再接 InteractiveOverlay 输入和焦点；完成 R2-P0-2 后再实现 Provider 设置页。真实捕获、API、SQLite 和规则计算仍不进入 Kimi 的 UI 切片。
