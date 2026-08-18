---
name: vainreef-fast-publish
description: Build general-utility Windows apps from natural-language requirements under a fixed local-first, free, individual-developer, MSIX and Microsoft Store contract. Project requests into Fast Mode when they fit the contract, identify infrastructure or compliance thresholds, and prepare an Advanced Mode handoff when a request needs a server, accounts, monetization, or restricted capabilities. Use when a user asks an AI coding agent to create, run, package, validate, or publish a Windows app through the Vainreef Fast Publish workflow.
---

# Vainreef Fast Publish

将用户的自然语言 App 想法落到固定的 Windows Golden Template，完成生成、运行、校验、MSIX 打包和 Microsoft Store 提交流程。把底层工具链冻结，把页面、业务和数据模型留给用户需求。

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
4. 需求落在契约内时直接实现。
5. 需求需要改变基础设施时，说明触发项，给出保留核心价值的本地版本，并把完整版本标记为 Advanced Mode。
6. 只有在用户确认后才把项目切换到 Advanced Mode；Fast Publish Skill 继续负责其中的 Fast Mode 版本。

用户沟通模板：

```text
完整需求需要 [服务器 / 账号体系 / 云同步 / 开发者统一 API / 收费 / 特殊 Capability]，这属于 Advanced Mode。
Fast Mode 可以先交付 [本地化投影]：核心价值保留，数据留在本机，应用保持免费并走 MSIX + Microsoft Store。
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

### 1. Normalize the request

提取以下信息，并把缺失项交给合理默认值：

- App 名称与一句话价值主张
- 页面和核心交互
- 本地数据模型
- 是否需要文件选择、网络或通知
- 目标架构：V1 固定 `win-x64`

将需求映射到 Golden Template，先列出计划文件，再开始改动。

### 2. Bootstrap the project

- 在 Windows 环境确认 `.NET SDK 10.x`、WinUI 模板、`winapp` CLI 和目标 Windows SDK。
- 使用模板或等价的官方 WinUI 项目创建基础工程。
- 记录 `dotnet --info`、`winapp --version`、Windows App SDK 和 WinUI template 版本。
- 保持项目名称、程序集名、包身份、显示名和 Store 名称一致。

CLI-first 基线命令：

```powershell
dotnet new winui-navview -n APP_NAME
cd APP_NAME
dotnet run
```

### 3. Implement the feature

按下面的顺序实现：

1. XAML 页面与可访问的控件标签。
2. code-behind 事件与输入校验。
3. 业务 Service。
4. 本地 JSON 或 SQLite 存储。
5. 浅色/深色主题、空状态、错误状态和首次运行示例数据。
6. 仅在需求明确时加入网络请求、文件能力或系统集成。

每个阶段都执行一次 `dotnet run`，让 Agent 先修复当前编译与运行问题，再继续增加功能。

### 4. Validate locally

- 先验证 Debug：`dotnet run`。
- 再验证 Release：`dotnet build -c Release` 或项目规定的 Release 命令。
- 测试首次启动、主要流程、空状态、重复启动、本地数据重载和窗口关闭/重开。
- 检查依赖清单、包身份、显示名称、图标和版本号。
- 记录测试机器、Windows 版本、架构和工具链版本。

### 5. Package as MSIX

开发运行和最终发布分开处理：

```powershell
dotnet publish -o ./publish
winapp pack ./publish --generate-cert --install-cert
```

本地开发证书用于测试安装；Store 提交使用 Store 认证后的签名链。管理员权限只用于本地测试证书与安装准备，向用户解释动作目的后再请求确认。

### 6. Prepare and publish to Store

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

- 需求摘要与采用的页面/数据模型。
- Fast Mode classification：direct、projected 或 Advanced Mode candidate。
- 若使用 projection，说明保留的核心价值与调整后的基础设施边界。
- 使用的模板 commit 与版本锁内容。
- 修改过的文件绝对路径。
- 执行过的命令与结果。
- Debug/Release/安装测试结果。
- MSIX 绝对路径、架构、版本和 SHA-256。
- 待用户确认的 Store 步骤。
- 后续可选功能，以及它们对依赖和发布变量的影响。

## References

按需读取：

- [version-lock.md](references/version-lock.md)：V1 版本锁定记录。
- [capability-boundary.md](references/capability-boundary.md)：按基础设施、数据、权限、隐私和商业化划分 Fast Mode。
- [official-sources.md](references/official-sources.md)：微软官方工具链、WinUI、.NET、SQLite 和 Store 资料。
