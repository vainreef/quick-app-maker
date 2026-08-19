---
name: vainreef-fast-publish
description: "Build general-utility Windows apps from natural-language requirements under a fixed local-first, free, individual-developer, MSIX and Microsoft Store contract. Use progressive discovery with safe defaults: create and update a living project README after every answer, ask only questions that materially affect product experience or feasibility, evaluate each requirement continuously, classify soft limits versus hard Advanced Mode boundaries, consult the capability registry before adding dependencies, and split simple user updates from technical run reports. Use when a user asks an AI coding agent to define, create, run, package, validate, or publish a Windows app through the Vainreef Fast Publish workflow."
---

# Vainreef Fast Publish

将用户的自然语言 App 想法落到固定的 Windows Golden Template，完成生成、运行、校验、MSIX 打包和 Microsoft Store 提交流程。把底层工具链冻结，把页面、业务和数据模型留给用户需求。

## First Phase: Progressive Discovery With Continuous Feasibility

每个新 App 项目先进入创意与需求明确阶段。第一问围绕“你想做什么”，让用户自由发挥想象；与此同时，Agent 在每轮内部检查固定技术栈与 Fast Mode 边界。创意理解和可行性思考属于同一条连续流程。详细流程见 [discovery-interview.md](references/discovery-interview.md)。

遵循以下规则：

1. 第一问使用：`你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。`
2. 每轮优先提出一个关键问题，根据上一轮答案自然深入；能安全推断的内容采用合理默认值，不把访谈变成固定问卷。
3. 用户给出第一段实质想法后，立即在当前工作目录创建暂定项目文件夹和 `README.md`。
4. 每次收到用户回答后，先更新 README，再同时进行需求理解与边界判断。
5. README 持续记录：当前想法、用户需要什么、用户目前不需要什么、新增创意、已确定内容、当前限制与提醒、用户对提醒的选择、仍待明确内容和每轮变更。
6. 新需求与当前技术栈、性能目标、Fast Mode 基础设施或 Store 路径存在明显张力时，立即提醒；提醒发生在对应想法出现的当轮。
7. 提醒使用用户易懂的语言，依次说明：用户想要的体验、当前路线擅长什么、按原方向继续可能出现什么结果、建议如何调整，并询问用户是否确认继续或接受调整。
8. 提醒前至少审视五个方面：当前技术栈是否匹配、性能目标是否现实、素材与内容制作量、在线或系统依赖、Store 发布变量。基于完整体验慎重判断，而不是只看关键词。
9. 将提醒分为 Soft Limit 与 Hard Boundary：Soft Limit 记录风险并取得用户选择后继续；Hard Boundary 切换到 Advanced Mode，Fast Mode 在该项停止。
10. 例如完整 3D 射击游戏触发技术栈与规模 Hard Boundary：说明当前路线更适合普通 Windows 工具和轻量界面，完整 3D 射击涉及实时画面、角色动画、物理、地图、音效与持续性能优化；给出游戏专用路线或轻量 2D/俯视角/射击训练投影。
11. 用户选择 Soft Limit 方向时，把已知限制、用户选择和后续影响写入 README；用户选择 Hard Boundary 方向时，把项目标记为 Advanced Mode candidate，并暂停 Fast Mode 工程动作。
12. 用户确认调整方案时，立即更新“用户需要什么、用户目前不需要什么、已确定内容与限制记录”，然后继续创意提问。
13. 识别用户的结束信号，例如“就是这样”“差不多了”“就这些”“这个方向可以”“开始吧”“按这个来”。
14. 收到结束信号后，停止发散，重新阅读完整 README，并用非常容易理解的语言复述整个项目以及用户已经确认过的取舍。
15. 最终复述只讲产品体验：要做什么、给谁用、打开后会发生什么、需要什么、目前省略什么、希望是什么感觉、怎样算做好。工程术语保持隐藏。
16. 复述结尾只问：`我理解的是这样，对吗？`
17. 用户确认后，将 README 状态更新为“需求与可行方向已确认”，进入工程实现阶段；Hard Boundary 项目保留需求记录，转入 Advanced Mode 处理。

连续流程为：

```text
自由创意 → 更新 README → 同轮可行性思考 → 必要时提醒并确认 → 继续提问 → 用户主动收口 → 通俗完整复述 → 用户确认 → 工程实现
```

## Skill Contract

进入 Fast Publish Mode 后，使用下面的技术栈。Agent 将技术栈视为契约，业务需求在契约之上实现。

| 层 | 固定选择 | 规则 |
| --- | --- | --- |
| 目标平台 | Windows 11 优先；Windows 10 1809+ 兼容范围 | Windows-first |
| CPU | x64 V1 | ARM64 进入后续版本 |
| 语言 | C# | 使用 .NET 生态 |
| Runtime | .NET 10 LTS | 固定 .NET 10.x，补丁版本跟随验证记录 |
| UI | WinUI 3 + XAML | 使用 Windows Fluent 控件与样式 |
| Windows 平台层 | Windows App SDK Stable | 每个 Skill release 锁定具体版本 |
| 项目基础 | Vainreef WinUI Golden Template | 从模板开始，业务代码填入既有结构 |
| 构建 | `dotnet` CLI | CLI-first |
| Windows 工具链 | `winapp` CLI | 锁定经过测试的版本；先读取 `winapp --version` |
| 包格式 | MSIX only | Store Fast Mode 固定 MSIX 路线 |
| 分发 | Microsoft Store | Store 资料和提交保留人工确认点 |
| 简单数据 | `System.Text.Json` + 本地文件 | 设置、小列表、偏好和轻量状态 |
| 结构化数据 | SQLite + `Microsoft.Data.Sqlite` | 仅在复杂查询、历史记录或大量数据时启用 |
| 架构 | XAML + code-behind + 简单 Service 层 | V1 默认保持短调用链 |
| 网络 | `HttpClient` | 按需求启用；标准库优先 |
| 第三方包 | Allowlist | 新增包先记录理由、版本和测试结果 |
| 后端 | 自建服务器：空 | 外部 API 按需求受控接入 |
| 商业化 | 免费应用 | 价格与内购进入独立版本 |

### 版本冻结

- 读取 [version-lock.md](references/version-lock.md)，将 `WINDOWS_APP_SDK_VERSION`、`WINAPP_CLI_VERSION`、`WINUI_TEMPLATE_VERSION` 和 SQLite 版本替换成经过 Windows 实机验证的值。
- 采用 Windows App SDK Stable channel。Stable/Preview/Experimental 的含义以 [Windows App SDK release channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/) 为准。
- 将 `winapp` 视为需要显式锁定的工具；官方当前将其标为 public preview，命令和特性可能变化。发布前记录 `winapp --version` 与 `winapp --help` 输出。
- 版本锁定后，Agent 依据 lock file 工作；升级动作单独形成 Skill release。

## Capability Boundary

Fast Publish Mode 以基础设施、数据流、权限和 Store 前提定义边界，而不是按“聊天 App、图片 App、天气 App”这类产品名称做排除。完整规则见 [capability-boundary.md](references/capability-boundary.md)。

### Fast Mode contract

把每个需求投影到下面的 Fast Mode Contract：

> **免费、本地、个人、通用；固定技术栈；Microsoft Store only。**

- **免费**：应用价格为 Free；付费 Offer、订阅、IAP、广告、License 和 payout 进入 Advanced Mode。
- **本地**：核心计算、JSON/SQLite 数据和用户内容默认留在 Windows 本机；自建服务器、云数据库、同步服务和 License Server 进入 Advanced Mode。
- **个人**：按普通个人开发者账号设计；公司主体、DUNS、组织审批和企业身份链路作为额外前提单独评估。
- **通用**：效率、创作、学习、生活、本地数据处理、API Client 和个人工具优先进入 Fast Mode；行业资质、特殊版权授权、金融/医疗/赌博运营链路进入 Advanced Mode。
- **权限**：使用普通窗口、文件选择器和本地存储；管理员、驱动、内核、系统服务、全局拦截、特殊系统数据和 Restricted Capability 进入 Advanced Mode。
- **隐私**：默认关闭遥测、广告追踪、账号体系和用户内容上传；具体 App 仍按当前 Store 规则检查隐私政策要求。
- **秘密**：公开 API 或最终用户自行填写的 API key 可以进入 Fast Mode；开发者自己的 key、统一额度、统一计费和隐藏凭据需要服务器边界。
- **网络**：没有自建服务器仍然可以联网；使用 `HttpClient` 访问稳定公开 API，或让用户提供自己的凭据。

### Boundary severity

#### Soft Limit

技术上仍然落在 Fast Mode，只是工作量、性能、依赖或测试范围明显增加。处理方式：

1. 当轮提醒用户影响。
2. 给出轻量化的实现建议。
3. 用户确认后继续探索与实现。
4. 将风险、默认值和用户选择写入 living README。

典型例子：复杂动画界面、大型本地数据处理、复杂 PDF 操作、较重的图片处理、经过验证的 native-backed 库或 bundled CLI。

#### Hard Boundary

完整需求改变 Fast Mode 的基础设施或发布前提。处理方式：

1. 当轮提醒用户，并用日常语言说明影响。
2. 将项目标记为 Advanced Mode candidate。
3. Fast Mode 在触发项停止工程推进。
4. 给出保留核心创意的 Fast Mode projection，或切换到 Advanced Mode 的明确入口。

典型例子：自建服务器、项目方密钥隐藏、账号云同步、统一收费、驱动/内核/管理员级功能、跨平台宿主、外部运行时安装要求、特殊行业资质和完整 3D 游戏引擎需求。

### Request triage

每次收到需求时执行：

1. 提取产品目标，不按产品类别做结论。
2. 检查服务器、账号、云同步、开发者秘密、收费、特殊权限、行业资质和个人信息数据流。
3. 为需求生成 Fast Mode projection：优先采用本地数据、用户输入、公开 API、文件选择器和免费 Store Offer。
4. 需求落在契约内时，把可行性结论写入已经存在的 living README。
5. 需求需要改变基础设施时，给出保留核心价值的第一版方案，并把完整版本标记为 Advanced Mode。
6. 用户确认可行方案后，更新 README 并进入工程实现阶段。

用户沟通模板：

```text
你想要的重点是：[核心体验]。
其中 [某项体验] 会让应用长期依赖另一套在线服务、统一账号、收费流程或更深层系统访问。
第一版我建议这样处理：[更轻量的具体体验]。
这样会保留：[保留内容]；暂时调整：[调整内容]。
这样调整可以吗？
```

### User prerequisites

- 采用 CLI-first 路径；Visual Studio 作为可选工具，Fast Publish 流程以 `dotnet`、WinUI templates 和 `winapp` 为主。
- Git 属于实现细节。Agent 可以读取本地仓库、下载 archive 或使用其他文件来源完成模板初始化。
- Windows 实机或 VM 是 WinUI、Package Identity、开发证书、MSIX 安装和 Store readiness 的验证环境。

### Stack boundary

Fast Mode 只有一套宿主底盘：C#/.NET 10/WinUI 3/Windows App SDK Stable/`dotnet`/`winapp`/MSIX/Store。WPF、WinForms、.NET MAUI、Electron、Tauri、Python GUI 宿主、C++ UI、EXE/MSI/NSIS/Inno Setup/Squirrel 属于技术栈切换信号；记录为 Advanced Mode candidate，并优先给出保留用户目标的 Fast Mode projection。经 Registry 验证的 CLI、native wrapper 或 embedded Python 只作为 C# Host 的受控 capability layer。

## Golden Template

Production Fast Mode 只使用 `/templates/windows-golden-template/` 中经过验证并由版本锁记录的 Golden Template。创建工程前检查模板版本、manifest 和 checksum：

- 模板目录缺失：暂停项目脚手架，记录 Skill installation repair。
- 版本或 checksum 不匹配：暂停项目脚手架，先修复模板安装。
- 模板状态仍为 draft：保持项目在需求/可行性阶段，等待模板 release。

开发实验可以单独建立草稿工程；Fast Publish 项目保持单一 Golden Template 来源。

模板保持以下目录稳定：

```text
App.xaml
MainWindow.xaml
Pages/
Models/
Services/
Storage/
Assets/
Package.appxmanifest
build/
store/
```

让 Agent 只在模板中填充业务功能。保持应用名称、窗口、资源、清单、构建和 Store 资料之间的一致性。

## Architecture Rules

- 小功能采用 `Page.xaml` → `Page.xaml.cs` → `Service` 的短链路。
- 轻量状态使用 `System.Text.Json` 写入本地文件。
- 结构化数据使用 `Microsoft.Data.Sqlite`；直接使用 SQL 与轻量服务层。
- 默认使用 `HttpClient` 访问公开 HTTP API。
- 默认把 MVVM Toolkit、DI 容器、EF Core、ORM、Repository 框架和动画包放在 Allowlist 之外；复杂度达到新架构门槛时，升级 Skill mode。
- 每个新增 NuGet 包写入依赖清单：包名、精确版本、用途、许可证、体积/原生依赖、验证结果。
- 让 App 首次打开即可说明价值，示例数据和空状态都可用。
- 首个 App 优先本地化、离线化和小功能闭环，降低 Store 变量。
- 默认关闭遥测、广告 SDK、远程用户画像和内容上传；涉及个人信息时读取当前 Store policy 并准备对应资料。

## Capability Registry and Dependency Ladder

使用 `capabilities/` 里的 Capability Registry 选择 PDF、图片、CSV、Office、压缩包、媒体、OCR、二维码和本地 AI 等能力。先找已验证条目，再决定实现方式；能力条目缺失或状态未验证时，记录为 registry gap，保持依赖决策待审。

保持 **C# Host First**：WinUI/C# 是宿主底盘；“Host First”允许经过验证的 CLI、native wrapper 或随 App 分发的 Python sidecar 作为受控能力层，形成 C# Host + Capability，而非自由换宿主技术栈。

按下面的阶梯逐级选择：

| Level | 能力来源 | 使用条件 |
| --- | --- | --- |
| 0 | .NET / Windows 内置能力 | 优先采用，默认路径 |
| 1 | Approved Managed NuGet | 纯托管 .NET，精确版本与许可证已登记，可自动采用 |
| 2 | Approved Native-backed NuGet | 包含 native DLL；必须完成 x64、MSIX、安装、启动与卸载验证 |
| 3 | Approved bundled CLI | 以子进程调用；必须验证打包、路径、退出码、日志、许可证与 Store 资料 |
| 4 | Embedded Python Runtime | .NET/native 生态明显缺少关键能力时采用；Python 与依赖随 App 分发，用户端保持一键安装体验 |
| 5 | External Runtime Requirement | 需要用户自行安装 Python、Node、Java、Rust 等；触发 Hard Boundary，进入 Advanced Mode |

Level 2–4 仍要求 C# Host、x64/MSIX 验证、依赖登记和可审计的运行报告。Level 5 作为 Advanced Mode 条件记录。

## Workflow

### 1. Start with open creativity and create the living README

读取 [discovery-interview.md](references/discovery-interview.md)，从用户已经表达的想法继续。首次提问聚焦“想做什么”，例如：

```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

用户给出第一段实质描述后：

1. 从描述中生成暂定项目名和小写连字符 `<app-slug>`；名称还在变化时标记为“暂定”。
2. 在当前工作目录检查同名路径，选择清晰且安全的项目目录名。
3. 创建 `<app-slug>/`。
4. 读取 [project-readme-template.md](assets/project-readme-template.md)，创建 `<app-slug>/README.md`。
5. 把用户原话中的核心想法写进“当前想法”和“本轮记录”。
6. 记录 README 绝对路径，然后提出下一轮创意问题。

用户最初的请求已经包含实质想法时，直接创建 living README，再追问最值得深入的创意点。

### 2. Iterate creatively and update README every round

每轮都执行同一个循环：

```text
读取 README
→ 理解本轮回答
→ 更新用户需要与当前省略
→ 检查技术栈和 Fast Mode 边界
→ 有明显风险时立即提醒并等待选择
→ 更新提醒与用户决定
→ 选择下一个创意缺口
→ 提出一个自然问题
```

每次更新至少检查：

- `当前想法`：现在整体要做什么。
- `用户需要什么`：明确提出、认可或反复强调的内容。
- `用户目前不需要什么`：用户排除、推迟或表达兴趣较低的内容。
- `使用画面`：打开以后看到什么、做什么、得到什么。
- `感觉与风格`：希望带给用户的感受。
- `新增创意`：本轮出现、还在探索的点子。
- `已确定内容`：用户已经明确认可的决定。
- `待明确内容`：下一轮真正值得问的问题。
- `当前限制与提醒`：本轮发现的技术栈、性能、联网、权限、商业化或发布风险。
- `用户对提醒的选择`：继续原方向、采用调整、交给高级路线或暂缓决定。
- `创意记录`：日期/轮次、本轮新增、修改、移除与用户原意摘要。

提问从产品体验逐步递进：

1. 想做什么、脑中画面和最吸引人的部分。
2. 谁会使用、什么时候打开。
3. 打开后第一眼看到什么。
4. 用户会点什么、输入什么、接下来发生什么。
5. 最希望保留的三件事。
6. 当前阶段希望省略或以后再加入的内容。
7. 风格、语气、名称和完成后的理想感觉。
8. 用户亲自体验时，怎样判断“这就是我想要的”。

使用产品语言交流，同时在内部持续检查工程适配度。风险会影响成品体验、成本、性能或发布稳定性时，当轮向用户说明；纯实现细节保持隐藏。

### 3. Detect the user stop signal and confirm the whole product

每轮先判断用户是否表达收口意图。典型语义包括：

- 就是这样、就这些、差不多了。
- 这个方向可以、按这个来、开始吧。
- 先做到这里、第一版这样就行。
- 其他你决定、我暂时没有补充。

识别到收口意图后：

1. 把本轮内容写入 README。
2. 将 README 状态改为“等待需求确认”。
3. 阅读 README 全文，处理重复项和前后变化，以最新用户表达为准。
4. 向用户完整复述项目，保持短句、具体场景和日常语言。
5. 复述内容只包括：
   - 这是一个什么 App。
   - 谁会用、什么时候用。
   - 打开以后会看到什么。
   - 用户会依次做什么、最后得到什么。
   - 用户明确需要什么。
   - 用户明确暂时省略什么。
   - 希望呈现什么感觉。
   - 怎样算第一版做好。
6. 此阶段隐藏技术栈、框架、存储方案、API 名称、Capability、MSIX 和构建工具。
7. 结尾询问：`我理解的是这样，对吗？`

用户补充或纠正时，回到 Stage 2，更新 README 并继续澄清。用户确认时，进入 Stage 4。

### 4. Finalize the continuously checked product brief

用户确认完整复述后：

1. 确认 README 已记录每一项重要限制、提醒和用户选择。
2. 将 README 状态更新为“需求与可行方向已确认”。
3. 把仍待工程验证的内容标为“制作中验证”，并保持用户易懂的描述。
4. 如项目名称在创意阶段发生变化，安全地重命名项目目录并更新 README 标题。
5. 进入技术工程阶段。

### 5. Bootstrap the technical project

- 读取 [version-lock.md](references/version-lock.md) 指向的 Toolchain Command Reference。
- 在 Windows 环境确认 lock file 中的 `.NET SDK`、WinUI 模板、`winapp` CLI 和目标 Windows SDK。
- 只从 Golden Template 创建基础工程，记录模板版本与 checksum。
- 按 Command Reference 记录工具版本、Windows 版本、架构和构建入口。
- 保持项目名称、程序集名、包身份、显示名和 Store 名称一致。

### 6. Implement the confirmed V1

按下面的顺序实现：

1. XAML 页面与可访问的控件标签。
2. code-behind 事件与输入校验。
3. 业务 Service。
4. 本地 JSON 或 SQLite 存储。
5. 浅色/深色主题、空状态、错误状态和首次运行示例数据。
6. 用户确认过的网络请求、文件能力或系统集成。

每个阶段都执行一次 Command Reference 中的开发运行入口，先处理当前编译与运行问题，再继续增加功能。新想法若改变已确认范围，先更新 README 并请用户确认。

### 7. Validate locally

- 按 Command Reference 验证 Debug 与 Release。
- 按 README 的 Acceptance Criteria 逐条验收。
- 测试首次启动、主要流程、空状态、重复启动、本地数据重载和窗口关闭/重开。
- 检查依赖清单、包身份、显示名称、图标和版本号。
- 记录测试机器、Windows 版本、架构和工具链版本。

### 8. Package as MSIX

开发运行和最终发布分开处理，使用版本锁指向的 packaging commands。

本地开发证书用于测试安装；Store 提交使用 Store 认证后的签名链。管理员权限只用于本地测试证书与安装准备，向用户解释动作目的后再请求确认。

### 9. Prepare and publish to Store

发布前检查：

- 包格式为 MSIX/MSIXBUNDLE。
- App manifest、Display Name、Publisher、版本与 Store 资料一致。
- Store listing 至少准备描述、图标和截图。
- 完成年龄评级问卷。
- 个人信息访问、收集或传输场景准备隐私政策 URL。
- 检查 manifest 中的 Capability；普通文件选择优先，Restricted Capability 与 elevation 进入 Advanced Mode 评估。
- 将 `APP_ID`、目标市场、价格和可见性留给用户确认。

按 Command Reference 执行 Store submission command，并把 `APP_ID`、目标市场、价格和可见性留给用户确认。

涉及 Microsoft 登录、身份验证、Partner Center 资料、管理员权限、年龄评级、政策问卷和最终提交时，暂停自动操作并等待用户完成当前确认点。其余生成、编译、检查、打包和日志整理由 Agent 继续执行。

## Dependency Gate

执行下列判断：

1. 先读取 `capabilities/` 中对应能力条目，确认 preferred implementation、Level、版本、许可证和测试状态。
2. .NET 自带能力优先：JSON 用 `System.Text.Json`，HTTP 用 `HttpClient`，文件用 BCL/Windows API。
3. SQLite 只在本地结构化数据确有需求时启用，并使用 `Microsoft.Data.Sqlite`。
4. Managed NuGet、native-backed NuGet、bundled CLI 和 embedded Python 只能按 Dependency Ladder 进入；每项记录精确版本和构建结果。
5. Native dependency、额外运行时、子进程、许可证变化、MSIX 清单变化或权限变化，都要在对应 capability 条目中完成 x64/MSIX 验证。
6. External Runtime Requirement 触发 Hard Boundary，Fast Mode 停止在该项。
7. 依赖升级与业务功能分开提交，方便定位 Store/打包回归。

## Output Contract

### User Output

向用户保持短、清楚、可行动：

- 当前 App 已完成的主要体验。
- 用户可直接验证的结果。
- MSIX 或测试安装包是否生成。
- 用户下一步只需要做什么。
- 尚待用户确认的登录、身份、Store 表单或政策步骤。

用户输出保持产品语言；版本号、命令、文件清单、日志和 hash 进入 Technical Run Report。

### Technical Run Report

将技术记录写入项目目录的 `build/run-report.md`：

- 用户确认的需求摘要、核心流程与验收标准。
- Fast Mode classification：direct、projected 或 Advanced Mode candidate。
- projection 或 Hard Boundary 的用户决定。
- 项目目录、README、模板版本与 checksum。
- 修改过的文件绝对路径。
- Command Reference 标识与执行结果。
- Debug/Release/安装测试结果。
- MSIX 绝对路径、架构、版本和 SHA-256。
- 依赖清单、Capability Registry 条目和许可证记录。
- Store 步骤、人工确认点和后续可选功能。

## References

按需读取：

- [discovery-interview.md](references/discovery-interview.md)：创意问题树、逐轮 README 更新、收口信号与通俗确认格式。
- [version-lock.md](references/version-lock.md)：V1 版本锁定记录。
- [capability-boundary.md](references/capability-boundary.md)：按基础设施、数据、权限、隐私和商业化划分 Fast Mode。
- [capabilities/registry.md](capabilities/registry.md)：Capability Registry 与实现阶梯。
- [toolchain/README.md](references/toolchain/README.md)：版本锁定的命令入口与版本目录规则。
- [official-sources.md](references/official-sources.md)：微软官方工具链、WinUI、.NET、SQLite 和 Store 资料。
- [project-readme-template.md](assets/project-readme-template.md)：第一段实质想法出现后创建，并在每轮持续更新的 living README 模板。
