# MahjongAgent

[![CI](https://github.com/qingduzhai/MahjongAgent/actions/workflows/ci.yml/badge.svg)](https://github.com/qingduzhai/MahjongAgent/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/qingduzhai/MahjongAgent?include_prereleases)](https://github.com/qingduzhai/MahjongAgent/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-3B82F6)](docs/releases/0.1.0-preview.1.md)

面向 Windows 的开源麻将观察 Agent。它持续截取用户明确选择的牌局窗口，将静态截图转换为结构化观察，并以可回放的牌局状态为基础提供规则、牌效、风险与出牌建议。

首个规则集聚焦武汉红中赖子杠。目标窗口可以是麻将游戏，也可以是播放完整牌局视频的播放器或浏览器；Agent 与多模态 Provider 始终处理单张截图，不上传整段视频。

> [!WARNING]
> 项目目前处于 `0.1.0-preview.1` 技术预览阶段，尚不可用于实际牌局。当前版本验证“窗口截图 → 多模态 Provider → 结构化 JSON”链路，不包含完整策略、持续监控和悬浮助手。

## 当前能力

| 能力 | 状态 | 说明 |
|---|---|---|
| Windows 桌面程序 | 可用 | .NET 10 / WPF，提供自包含 win-x64 发布 |
| 任意窗口选择与截图预览 | 可用 | 支持普通可见、未最小化窗口 |
| OpenAI-Compatible Provider | 可用 | Base URL、Path、模型、认证、JsonSchema/JsonObject、图片 detail |
| Provider 能力探测 | 可用 | 验证鉴权、图片输入和结构化输出 |
| API Key 安全保存 | 可用 | 仅存 Windows Credential Manager |
| 单张截图结构化识别 | 实验性 | 用于验证视觉链路，不作为出牌依据 |
| C# Agent 状态机与 checkpoint | 可用 | 不依赖 LangGraph，支持暂停、恢复和故障状态 |
| 持续变化检测与牌局事件重建 | 开发中 | 事件日志、状态快照和重启恢复基础已实现，持续捕获尚未接入发布版 |
| 武汉麻将完整策略 | 开发中 | 规则、牌效和防放铳策略尚未完成 |
| 悬浮助手 | 设计完成 | UI v1.3 已锁定，尚未进入正式实现 |

完整版本说明见 [0.1.0-preview.1 发布说明](docs/releases/0.1.0-preview.1.md)。

## 快速开始

### 使用预览版

1. 从 GitHub Releases 下载 `MahjongAgent-v0.1.0-preview.1-win-x64.zip`；
2. 解压并运行 `MahjongAgent.Platform.Windows.exe`；
3. 选择包含麻将牌局画面的窗口并截取当前画面；
4. 在右侧填写支持图片输入和结构化输出的 OpenAI-Compatible Provider；
5. 输入 API Key，点击“测试连接并保存”；
6. 测试成功后可执行一次实验性截图识别。

发布包是 Windows x64 自包含版本，不要求用户另外安装 .NET。当前尚未代码签名，Windows 可能显示未知发布者提示。

### 从源码运行

要求：

- Windows 10 2004 或更高版本；
- .NET 10 SDK；
- PowerShell 7 或 Windows PowerShell。

```powershell
git clone https://github.com/qingduzhai/MahjongAgent.git
cd MahjongAgent
dotnet restore MahjongAgent.slnx
dotnet test MahjongAgent.slnx --configuration Release
dotnet run --project src/MahjongAgent.Platform.Windows
```

生成自包含发布包：

```powershell
dotnet publish src/MahjongAgent.Platform.Windows/MahjongAgent.Platform.Windows.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishProfile=win-x64
```

## Provider 配置

标准 OpenAI-Compatible 配置由以下字段组成：

```text
Base URL: https://api.example.com/v1/
Chat Completions Path: chat/completions
Model ID: 支持图片输入与结构化输出的模型
Authentication: Bearer / Header / None
Structured Output: JsonSchema / JsonObject
```

注意：Base URL 必须以 `/` 结尾，Path 不能以 `/` 开头。

API Key 不写入配置文件、SQLite、日志或发布包。应用测试连接成功后，Key 仅保存到 Windows Credential Manager；再次测试时可以留空继续使用已保存的 Key。

## 架构原则

- `GameEvent` 是牌局事实来源，`GameState` 是可重建快照；
- 多模态模型只产生候选观察，必须经过时序、牌数和规则校验；
- 胡牌、合法动作、牌效和风险尽量由确定性 C# 代码计算；
- Agent 使用强类型状态机控制合法迁移，LLM 不负责决定工作流；
- 不读取游戏进程内存，不注入游戏，不拦截协议，不自动点击或出牌；
- 视觉样本、记忆和策略变更必须可追踪、可审核和可回滚；
- 默认不保存完整窗口截图，不上传用户未确认的画面。

```text
Window Screenshot
  → Change Detection
  → Multimodal Observation (candidate)
  → Observation Resolver
  → GameEvent / GameState
  → Rules / Strategy / Memory
  → Dashboard / Overlay
```

关于不以 LangGraph 为核心运行时的理由，见 [ADR-0004](docs/adr/0004-csharp-agent-orchestration.md)。

## 项目结构

```text
src/
  MahjongAgent.Core                       领域模型、事件和牌局状态
  MahjongAgent.Agent                      Agent 编排、节点与 Provider 管理
  MahjongAgent.Capture.Abstractions       截图帧与时间轴协议
  MahjongAgent.Perception                 多模态观察协议
  MahjongAgent.Providers.OpenAICompatible OpenAI-Compatible Provider
  MahjongAgent.Rules.Wuhan                武汉麻将规则族
  MahjongAgent.Strategy                   牌效、风险和候选动作
  MahjongAgent.Storage                    SQLite 持久化
  MahjongAgent.Platform.Windows           WPF、窗口截图和 Credential Manager
tests/                                    单元、集成与架构测试
docs/                                     架构、ADR、UI 与发布说明
```

## 路线图

- [x] 标准麻将牌编码与武汉牌集基础；
- [x] OpenAI-Compatible 图片与结构化输出 Provider；
- [x] Provider 探测、SQLite 档案和 Credential Manager；
- [x] C# Agent 状态机、checkpoint 与节点执行；
- [x] Windows 窗口选择、截图预览和单图视觉验证；
- [ ] Windows Graphics Capture 与自适应截图频率；
- [ ] Observation Schema、时序解析与 `GameState`；
- [ ] 武汉红中赖子杠合法动作、牌效和风险策略；
- [ ] WindowProfile / VisualProfile 学习与纠错；
- [ ] 悬浮助手、托盘和局内主动提示；
- [ ] 固定牌局评测集、复盘与长期记忆晋升。

## 文档

- [总体架构](docs/architecture.md)
- [Agent 与长期记忆后端设计](docs/backend-agent-memory.md)
- [多模态 Prompt 与长期记忆检索](docs/multimodal-prompt-and-memory-retrieval.md)
- [桌面技术选型](docs/adr/0001-desktop-stack.md)
- [产品界面职责](docs/adr/0002-product-surfaces.md)
- [统一截图序列来源](docs/adr/0003-unified-frame-sources.md)
- [C# Agent 编排决策](docs/adr/0004-csharp-agent-orchestration.md)
- [UI v1.3 交付](docs/ui/round-1/README.md)
- [安全策略](SECURITY.md)
- [贡献指南](CONTRIBUTING.md)

## 贡献

欢迎提交 Issue 和 Pull Request。开始开发前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，安全问题请按 [SECURITY.md](SECURITY.md) 私下报告，不要公开提交包含 API Key、截图或个人信息的 Issue。

## 合规说明

本项目仅提供观察、训练和决策提示能力。使用者有责任遵守具体游戏和平台的服务条款。项目不提供游戏注入、内部数据读取、协议拦截、自动点击或自动出牌能力。

## License

[MIT](LICENSE)
