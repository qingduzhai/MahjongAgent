# MahjongAgent

MahjongAgent 是一个面向 Windows 桌面的开源麻将观察 Agent。它可以读取用户明确选择的麻将窗口或完整牌局视频，通过多模态视觉将画面变化转换为结构化牌局事件，维护本局状态，并提供牌效、规则、风险和出牌建议。

产品采用双界面形态：主窗口是配置、运维和局内可视化仪表盘；悬浮助手是牌局进行时的核心交互界面。

首个规则集聚焦武汉红中赖子杠。Demo 使用多模态模型完成陌生窗口标定和牌面理解，并通过持久化的 `WindowProfile` 复用不同游戏界面的布局、皮肤和规则记忆。

## 当前状态

项目处于设计与骨架阶段，尚不可用于实际牌局。

## 设计原则

- 结构化 `GameState` 是唯一权威牌局记忆；
- 多模态模型只产生候选观察，必须经过时序和规则校验；
- 视频和实时窗口只是可替换的画面源，后续感知、状态和策略链路完全复用；
- 不读取游戏进程内存，不注入游戏，不自动点击或出牌；
- 核心规则、策略和记忆保持跨平台，Windows 能力置于平台外壳；
- 视觉档案和用户纠错可持久化、可审计、可回滚；
- 默认不保存完整窗口截图，不上传非牌局区域。

## 技术栈

- .NET 10 LTS / C#；
- WPF Windows 桌面外壳；
- 统一 `IFrameSource`，首批支持视频回放并预留 Windows Graphics Capture；
- SQLite；
- 多模态 Provider 抽象；
- Python 仅用于后续离线数据和模型训练。

## 仓库结构

```text
src/
  MahjongAgent.Core                  跨平台领域模型和事件状态
  MahjongAgent.Agent                 Agent 编排与工具调度
  MahjongAgent.Capture.Abstractions  跨平台画面源、帧与时间轴接口
  MahjongAgent.Perception            多模态观察与校验协议
  MahjongAgent.Providers.OpenAICompatible  OpenAI 兼容多模态 Provider
  MahjongAgent.Rules.Wuhan           武汉麻将规则族
  MahjongAgent.Strategy              牌效、风险和候选动作
  MahjongAgent.Storage               档案、事件和快照持久化
  MahjongAgent.Platform.Windows      WPF、窗口捕获和悬浮层
tests/
  MahjongAgent.Agent.Tests
  MahjongAgent.Core.Tests
  MahjongAgent.Platform.Windows.Tests
  MahjongAgent.Providers.OpenAICompatible.Tests
  MahjongAgent.Rules.Wuhan.Tests
  MahjongAgent.Storage.Tests
  MahjongAgent.Architecture.Tests
docs/
  architecture.md                    总体设计方案
  adr/                               架构决策记录
  ui/                                UI 提示词和审核资料
```

## 构建要求

- Windows 10 2004 或更高版本；
- .NET 10 SDK；
- Visual Studio 2026 或支持 .NET 10 的等效工具链。

```powershell
dotnet restore MahjongAgent.slnx
dotnet build MahjongAgent.slnx
```

## 文档

- [总体设计方案](docs/architecture.md)
- [Agent 与长期记忆后端设计](docs/backend-agent-memory.md)
- [桌面技术选型](docs/adr/0001-desktop-stack.md)
- [产品界面职责](docs/adr/0002-product-surfaces.md)
- [Kimi UI 设计提示词 v2](docs/ui/kimi-ui-prompt-v2.md)
- [UI 审核清单](docs/ui/ui-review-checklist.md)
- [UI 协作边界](docs/ui/handoff-contract.md)
- [第一轮 UI 设计交付](docs/ui/round-1/README.md)
- [第一轮 UI 审核结论](docs/ui/round-1/review-codex.md)

## 合规说明

项目仅提供观察、训练和决策提示能力。使用者有责任遵守具体游戏和平台的服务条款。项目不提供游戏注入、内部数据读取、协议拦截或自动操作能力。

## License

[MIT](LICENSE)
