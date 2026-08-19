---
name: vainreef-fast-publish
description: Build general-utility Windows apps from natural-language requirements under a fixed local-first, free, individual-developer, MSIX and Microsoft Store contract. Begin with an open creative interview about what the user wants to make, create a living project README after the first substantive idea, update it after every answer with what the user wants and does not want, detect when the user says the idea is complete, confirm the whole product in plain non-technical language, and only then run feasibility analysis. Use when a user asks an AI coding agent to define, create, run, package, validate, or publish a Windows app through the Vainreef Fast Publish workflow.
---

# Vainreef Fast Publish

将用户的自然语言 App 想法落到固定的 Windows Golden Template，完成生成、运行、校验、MSIX 打包和 Microsoft Store 提交流程。把底层工具链冻结，把页面、业务和数据模型留给用户需求。

## Mandatory First Phase: Creative Requirement Discovery

每个新 App 项目先进入创意与需求明确阶段。第一问围绕“你想做什么”，让用户自由发挥想象；此时先理解产品体验，不把想法强行套进“解决麻烦”的叙事。详细流程见 [discovery-interview.md](references/discovery-interview.md)。

遵循以下强制规则：

1. 第一问使用：`你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。`
2. 每轮优先提出一个关键问题，根据上一轮答案自然深入；问题围绕用户想看到什么、点什么、发生什么、保留什么感觉。
3. 用户给出第一段实质想法后，立即在当前工作目录创建暂定项目文件夹和 `README.md`。
4. 每次收到用户回答后，先更新 README，再提出下一问。README 持续记录：当前想法、用户需要什么、用户目前不需要什么、新增创意、已确定内容、仍待明确内容和每轮变更。
5. 访谈期间把 README 当作活的创意记录，允许推翻、扩展、缩小和重新命名；新回答覆盖旧理解时，保留一条简短变更记录。
6. 识别用户的结束信号，例如“就是这样”“差不多了”“就这些”“这个方向可以”“开始吧”“按这个来”。用户随时可以结束创意访谈。
7. 收到结束信号后，停止继续发散问题，重新阅读完整 README，并用非常容易理解的语言复述整个项目。
8. 需求确认复述只讲：要做什么、给谁用、打开后会发生什么、用户需要什么、用户目前不需要什么、希望是什么感觉、怎样算做好。此处保持产品语言，隐藏技术栈、框架、打包、数据库、权限名和发布工具。
9. 复述结尾只问：`我理解的是这样，对吗？`
10. 用户确认后，把 README 状态更新为“需求已确认”，进入独立的可行性分析阶段。
11. 可行性分析发现需要调整时，用生活化语言说明原因、保留内容和调整内容，然后询问：`这样调整可以吗？`
12. 用户确认可行方案后，再进入工程实现阶段。

阶段顺序固定为：

```text
自由创意 → 逐轮提问 → 每轮更新 README → 用户主动收口 → 通俗完整复述 → 用户确认 → 可行性分析 → 调整确认 → 工程实现
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

把每个需求投影到下面八个词：

> **免费、本地、个人、通用；固定技术栈；Microsoft Store only。**

- **免费**：应用价格为 Free；付费 Offer、订阅、IAP、广告、License 和 payout 进入 Advanced Mode。
- **本地**：核心计算、JSON/SQLite 数据和用户内容默认留在 Windows 本机；自建服务器、云数据库、同步服务和 License Server 进入 Advanced Mode。
- **个人**：按普通个人开发者账号设计；公司主体、DUNS、组织审批和企业身份链路作为额外前提单独评估。
- **通用**：效率、创作、学习、生活、本地数据处理、API Client 和个人工具优先进入 Fast Mode；行业资质、特殊版权授权、金融/医疗/赌博运营链路进入 Advanced Mode。
- **权限**：使用普通窗口、文件选择器和本地存储；管理员、驱动、内核、系统服务、全局拦截、特殊系统数据和 Restricted Capability 进入 Advanced Mode。
- **隐私**：默认关闭遥测、广告追踪、账号体系和用户内容上传；具体 App 仍按当前 Store 规则检查隐私政策要求。
- **秘密**：公开 API 或最终用户自行填写的 API key 可以进入 Fast Mode；开发者自己的 key、统一额度、统一计费和隐藏凭据需要服务器边界。
- **网络**：没有自建服务器仍然可以联网；使用 `HttpClient` 访问稳定公开 API，或让用户提供自己的凭据。

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

Fast Mode 只有一套底盘：C#/.NET 10/WinUI 3/Windows App SDK Stable/`dotnet`/`winapp`/MSIX/Store。WPF、WinForms、.NET MAUI、Electron、Tauri、Python GUI、C++ UI、EXE/MSI/NSIS/Inno Setup/Squirrel 属于技术栈切换信号；记录为 Advanced Mode candidate，并优先给出保留用户目标的 Fast Mode projection。

## Golden Template

优先使用 `/templates/windows-golden-template/` 中经过验证的模板；模板准备完成前，使用官方 WinUI 模板建立等价结构，并把差异记录在版本锁文件中。

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
读取 README → 理解本轮回答 → 更新 README → 选择当前最关键的创意缺口 → 提出一个自然问题
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

使用产品语言交流。对尚未影响用户体验的工程问题留到可行性分析阶段。

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

用户补充或纠正时，回到 Stage 2，更新 README 并继续澄清。用户确认时，将 README 状态改为“需求已确认”，进入 Stage 4。

### 4. Run feasibility analysis as a separate checkpoint

需求确认后，再读取 [capability-boundary.md](references/capability-boundary.md) 做内部判断：

- `direct`：用户确认的完整想法可以原样进入 Fast Mode。
- `projected`：核心体验保留，部分实现需要调整为更轻量的版本。
- `advanced-candidate`：完整体验需要持续在线服务、跨设备账号、统一代付能力、收费体系或更深层系统访问。

向用户解释时继续使用生活化语言，先给结论，再讲影响：

```text
你确认的核心体验是：[核心体验]。
其中 [部分内容] 会带来 [用户能理解的影响]。
我建议第一版这样处理：[调整后的体验]。
这样会保留：[保留内容]；暂时调整：[调整内容]。
这样调整可以吗？
```

用户确认后：

1. 更新 README 的“可行性结论”和“用户确认的调整”。
2. 把当前版本标记为“可进入实现”。
3. 如项目名称在创意阶段发生变化，安全地重命名项目目录并更新 README 标题。
4. 再进入技术工程阶段。

### 5. Bootstrap the technical project

- 在 Windows 环境确认 `.NET SDK 10.x`、WinUI 模板、`winapp` CLI 和目标 Windows SDK。
- 使用 Golden Template 或等价的官方 WinUI 项目创建基础工程。
- 记录 `dotnet --info`、`winapp --version`、Windows App SDK 和 WinUI template 版本。
- 保持项目名称、程序集名、包身份、显示名和 Store 名称一致。

CLI-first 基线命令：

```powershell
dotnet new winui-navview -n APP_NAME
cd APP_NAME
dotnet run
```

### 6. Implement the confirmed V1

按下面的顺序实现：

1. XAML 页面与可访问的控件标签。
2. code-behind 事件与输入校验。
3. 业务 Service。
4. 本地 JSON 或 SQLite 存储。
5. 浅色/深色主题、空状态、错误状态和首次运行示例数据。
6. 用户确认过的网络请求、文件能力或系统集成。

每个阶段都执行一次 `dotnet run`，先处理当前编译与运行问题，再继续增加功能。新想法若改变已确认范围，先更新 README 并请用户确认。

### 7. Validate locally

- 验证 Debug：`dotnet run`。
- 验证 Release：`dotnet build -c Release` 或项目规定的 Release 命令。
- 按 README 的 Acceptance Criteria 逐条验收。
- 测试首次启动、主要流程、空状态、重复启动、本地数据重载和窗口关闭/重开。
- 检查依赖清单、包身份、显示名称、图标和版本号。
- 记录测试机器、Windows 版本、架构和工具链版本。

### 8. Package as MSIX

开发运行和最终发布分开处理：

```powershell
dotnet publish -o ./publish
winapp pack ./publish --generate-cert --install-cert
```

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

CLI 提交基线：

```powershell
winapp store publish ./*.msix --appId APP_ID
```

涉及 Microsoft 登录、身份验证、Partner Center 资料、管理员权限、年龄评级、政策问卷和最终提交时，暂停自动操作并等待用户完成当前确认点。其余生成、编译、检查、打包和日志整理由 Agent 继续执行。

## Dependency Gate

执行下列判断：

1. .NET 自带能力优先：JSON 用 `System.Text.Json`，HTTP 用 `HttpClient`，文件用 BCL/Windows API。
2. SQLite 只在本地结构化数据确有需求时启用，并使用 `Microsoft.Data.Sqlite`。
3. 每个新增包都要进入 Allowlist，记录精确版本和构建结果。
4. Native dependency、需要额外运行时、会改变 MSIX 清单或增加权限的包，先进入人工评审。
5. 依赖升级与业务功能分开提交，方便定位 Store/打包回归。

## Output Contract

每次执行结束时，输出：

- 用户确认的需求摘要、核心流程与验收标准。
- Fast Mode classification：direct、projected 或 Advanced Mode candidate。
- 若使用 projection，说明保留的核心价值与调整后的基础设施边界。
- 新项目目录与 README 的绝对路径。
- 使用的模板 commit 与版本锁内容。
- 修改过的文件绝对路径。
- 执行过的命令与结果。
- Debug/Release/安装测试结果。
- MSIX 绝对路径、架构、版本和 SHA-256。
- 待用户确认的 Store 步骤。
- 后续可选功能，以及它们对依赖和发布变量的影响。

## References

按需读取：

- [discovery-interview.md](references/discovery-interview.md)：创意问题树、逐轮 README 更新、收口信号与通俗确认格式。
- [version-lock.md](references/version-lock.md)：V1 版本锁定记录。
- [capability-boundary.md](references/capability-boundary.md)：按基础设施、数据、权限、隐私和商业化划分 Fast Mode。
- [official-sources.md](references/official-sources.md)：微软官方工具链、WinUI、.NET、SQLite 和 Store 资料。
- [project-readme-template.md](assets/project-readme-template.md)：第一段实质想法出现后创建，并在每轮持续更新的 living README 模板。
