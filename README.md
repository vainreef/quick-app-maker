# Vainreef Quick App Maker

> 用一个可复用的 Skill，把“说一句话做 App”推进到“生成、测试、打包、提交 Microsoft Store”。

这个仓库是 **Skill 工作台 + Windows 测试 Build 仓库**：

- `skills/`：存放可安装、可复用的 Codex/AI Agent Skill。
- `templates/`：存放 Golden Template 候选、经过验证的模板与相关资源。
- `toolchain/`：存放已锁定版本的安装资源、版本和来源记录。
- `apps/test-builds/`：预留每个测试 App 的 MSIX、校验信息与构建记录归档位置。
- `docs/research/`：存放平台、工具链与发布流程的研究记录。

## 新 Windows 电脑初始化：Agent 必须先走这里

本节是仓库级前置流程，优先级高于产品创意访谈和 App 生成功能。用户把仓库地址交给 Agent 后，Agent 先读取本节，准备开发工具；工具链完成验证后，再进入 App 需求发现。

初始化目标是：**下载任务多线程并发；Git 下载完成后最先安装并立即 clone；clone 期间启动 .NET SDK，clone 完成后立即加入仓库内 WinAppCLI 安装；WinUI 模板在 .NET 就绪后立即安装；工具链完成后马上交接 Skill。**

当前初始化模式：`parallel-download-git-first-parallel-post-git-install`

### 初始化顺序

```text
读取 README
→ 读取工具链下载清单
→ 检查 Windows、架构、磁盘空间和已有版本
→ 启动多线程下载任务
→ Git 下载完成后立即安装
→ Git 可用后立即 clone 仓库
→ 其他下载继续并发运行
→ Git 就绪后并行 clone 和安装 .NET SDK
→ clone 完成后立即安装仓库内 WinAppCLI
→ .NET 就绪后立即安装 WinUI 模板
→ 验证 Git、.NET、WinUI 模板、NuGet 和 winapp
→ 写入 bootstrap 报告
→ 进入 App 需求发现
```

### 第一阶段：下载批次

初始化时先创建本地缓存目录，例如：

```text
<workspace>/build/bootstrap-cache/<toolchain-release>/
```

PowerShell 示例：

```powershell
$cache = Join-Path (Get-Location) 'build/bootstrap-cache/TOOLCHAIN_RELEASE'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
```

下载批次至少包含以下资源：

| 资源 | 用途 | 当前状态 |
| --- | --- | --- |
| Git for Windows x64 | 克隆、更新和提交仓库 | 真实 Windows 会话下载和安装成功 |
| .NET 10 SDK x64 | 创建和编译 WinUI 项目 | 真实 Windows 会话下载和安装成功 |
| WinUI C# template `.nupkg` | `dotnet new winui-navview` 和 Golden Template 维护 | 真实 Windows 会话安装成功 |
| WinAppCLI x64 | 运行、打包、证书和 MSIX | 0.6.1 x64 MSIX 随 Gitee 仓库 clone 获取，安装队列待完成 |
| Golden Template 依赖包 | Windows App SDK、BuildTools 和项目还原 | 等待 NuGet 源或本地 feed |
| WinApp/Windows App SDK 缓存 | 首次运行和打包所需的额外资源 | 按实机验证结果纳入 |

### 真实 Windows 会话中已经验证的下载方式

以下地址和命令来自一次 Windows 11 24H2 x64 实机流程记录。它们进入多线程下载队列；单项下载完成后立即进入安装队列，Git 任务拥有最高安装优先级。

#### 1. Git for Windows：国内直链下载成功

```text
版本：2.47.1.windows.1
文件：Git-2.47.1-64-bit.exe
地址：https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/Git-2.47.1-64-bit.exe
```

下载：

```powershell
$url = 'https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/Git-2.47.1-64-bit.exe'
$out = Join-Path $cache 'Git-2.47.1-64-bit.exe'
Invoke-WebRequest -Uri $url -OutFile $out
Get-Item $out | Select-Object FullName, Length
```

会话结果：文件约 69.1 MB；静默安装退出码为 0；`git --version` 显示 `2.47.1.windows.1`。

#### 2. .NET 10 SDK：微软 CDN 下载成功

```text
版本：10.0.400
架构：win-x64
文件：dotnet-sdk-10.0.400-win-x64.exe
地址：https://download.microsoft.com/download/0130d68c-c9c7-4114-a96c-ccdd476c6493/bd77bbcc-651a-4809-bd90-ca757cf4d5e8/dotnet-sdk-10.0.400-win-x64.exe
```

下载：

```powershell
$url = 'https://download.microsoft.com/download/0130d68c-c9c7-4114-a96c-ccdd476c6493/bd77bbcc-651a-4809-bd90-ca757cf4d5e8/dotnet-sdk-10.0.400-win-x64.exe'
$out = Join-Path $cache 'dotnet-sdk-10.0.400-win-x64.exe'
Invoke-WebRequest -Uri $url -OutFile $out
Get-Item $out | Select-Object FullName, Length
```

也可以使用已经验证过的 winget 源下载方式：

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget `
  --accept-source-agreements --accept-package-agreements --silent
```

会话结果：官方安装文件约 205 MB；直接运行安装器退出码为 0；`dotnet --list-sdks` 显示 `10.0.400`。README 后续优先采用直链批量下载，winget 作为备用方式，并固定 `--source winget`。

#### 3. WinUI C# 模板：NuGet 安装成功

```text
包名：Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
版本：0.0.6-alpha
```

会话中成功执行的安装命令：

```powershell
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates@0.0.6-alpha
```

下载批次采用本地包安装时，使用对应的 NuGet flat-container 文件：

```text
https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk.winui.csharp.templates/0.0.6-alpha/microsoft.windowsappsdk.winui.csharp.templates.0.0.6-alpha.nupkg
```

下载完成后从本地安装：

```powershell
dotnet new install .\microsoft.windowsappsdk.winui.csharp.templates.0.0.6-alpha.nupkg
dotnet new list winui
```

会话结果：模板安装成功，`winui-navview` 已注册；包体积约 373 KB。该版本属于 alpha，初始化始终使用固定版本。

#### 4. WinAppCLI：只使用仓库固定副本

```text
版本：0.6.1
架构：x64
文件：winappcli_x64.msix
```

仓库内固定副本：

```text
toolchain/winapp-cli/0.6.1/winappcli_x64.msix
```

初始化时先通过 Gitee clone 获取仓库，随后从 `toolchain/winapp-cli/0.6.1/winappcli_x64.msix` 复制到 bootstrap cache，并立即启动 WinAppCLI 安装任务。

WinAppCLI 的 bootstrap 来源策略固定为：

```text
Gitee clone 得到的仓库固定副本
→ 本地 bootstrap cache
```

安装阶段完全跳过 GitHub、GitHub Release、GitHub 网络探测和 `winget Microsoft.WinAppCli`。仓库 clone 成功后，WinAppCLI 直接使用仓库文件；仓库 clone 失败时记录 Gitee clone 错误并结束 bootstrap 分支。

#### 5. 现阶段仍待补齐的下载项

真实会话已经覆盖 Git、.NET SDK、WinUI 模板和仓库固定版 WinAppCLI。第一次编译 Golden Template 时，还要根据实际生成的 `.csproj` 收集并锁定：

```text
Microsoft.WindowsAppSDK
Microsoft.Windows.SDK.BuildTools
Microsoft.Windows.SDK.BuildTools.WinApp
以及它们的传递依赖
```

这些包放入同一下载批次，优先从本地 NuGet feed 或国内 NuGet 源获取，restore 阶段只使用已下载的缓存。

### 下载批次的来源优先级

```text
仓库内固定副本
→ 本地缓存
→ README/工具链锁文件中的国内直链
→ 已配置的国内 NuGet 源
→ 固定版本的官方备用地址
```

所有资源的版本、文件名、大小和来源进入工具链锁定清单；地址尚未填入前，保持为待配置项，不由 Agent 临时猜测包名或自行切换版本。

下载规则：

1. 所有资源先写入 `.part` 临时文件，下载完成后再改为正式文件名。
2. 使用多线程下载池；每个资源一个独立 worker，至少同时运行 4 个下载 worker。
3. Git worker 设置为高优先级，但其他资源同步开始下载，不等待 Git 下载结束才启动。
4. 主下载源、备用下载源和官方源只针对同一个固定版本，不跨版本替换。
5. 每项下载命令以进程退出码、输出文件存在和安装器自身结果作为完成依据，并把状态写入报告。
6. 单项下载失败时，只重试该资源或切换同版本备用源，其他下载继续运行。

下载执行器要求：

```text
download_workers = 3
download_mode = concurrent
download_worker_process = independent-terminal
controller_process = separate-terminal
one_artifact_per_terminal = true
git_priority = highest
install_mode = parallel-after-git
install_workers_after_git = 2
install_worker_process = independent-terminal
install_start = after-git-installed
start_all_workers_before_wait = true
progress_poll_interval_seconds = 1
```

每个下载资源必须使用一个独立 PowerShell/cmd 终端进程；控制终端只负责启动和轮询。下载器使用 3 个多线程 worker：Git、.NET SDK、WinUI template。WinAppCLI 不创建下载 worker，clone 后直接读取仓库固定副本。实现可以使用 `Start-Process powershell.exe`、Windows Terminal `wt.exe new-tab`、多个 `curl.exe`/BITS 子进程，但每个下载资源必须拥有独立 PID、独立日志和独立状态文件。

执行器控制顺序：

```text
1. 启动一个控制终端和 3 个独立下载终端
2. Git、.NET、WinUI template 各自占用一个下载终端
3. 控制终端每 1 秒读取各终端的 PID、状态文件和进度
4. Git 下载终端完成后，立即关闭该下载终端并启动 Git 安装终端
5. Git 安装终端完成后，立即启动 clone 终端和 .NET 安装终端
6. clone 完成后，从仓库复制 WinAppCLI 并启动安装终端；.NET 安装终端继续运行
7. .NET 安装终端完成后，启动 WinUI 模板安装终端
8. 工具链完成后，控制终端交接 Skill
```

Agent 的工具调用应体现这一点：同一轮启动多个独立终端进程；控制终端不承担下载；Git 安装后同时启动 clone 和 .NET 安装终端；clone 完成后立即启动仓库内 WinAppCLI 安装终端；WinUI 模板等待 .NET 就绪。一个终端承载全部下载或全部安装，会退化为串行流程。

### 第二阶段：Git 优先、Git 之后并行安装

Git 是唯一的绝对前置。Git 安装完成后立即 clone 并同时安装 .NET SDK；clone 完成后立即安装仓库内 WinAppCLI，.NET 安装继续运行；WinUI 模板在 .NET 可用后由新的独立终端立即安装。

安装关系如下：

1. **Git for Windows**：下载进程完成后立即安装；安装后刷新当前 PowerShell 进程的 PATH，并用 `git --version` 验证。
2. **获取仓库**：Git 验证通过后立即执行：

   ```powershell
   git clone https://gitee.com/freevian/quick-app-maker.git
   ```

   已存在工作区时，使用该工作区的远程地址和当前分支继续执行。
3. **.NET 10 SDK**：Git 安装完成后立即启动，约 205 MB。
4. **WinAppCLI**：clone 完成后从仓库固定副本立即启动安装，约 18 MB；.NET 安装继续运行。
5. **WinUI C# template**：包可以提前下载；安装命令等待 .NET SDK 可用，随后立即执行。
6. **Golden Template 依赖包**：下载完成后立即放入本地 NuGet feed；restore 延后到真实 App 项目创建后执行。
7. **交接 Skill**：工具链满足条件后结束 bootstrap，进入 `vainreef-fast-publish` 的需求发现流程。

安装选择规则：

- Git 安装完成前，其他安装任务保持等待，下载任务持续运行。
- Git 安装完成后，clone 和 .NET SDK 安装分别占用独立终端并同时运行。
- clone 完成后立即使用仓库内 `toolchain/winapp-cli/0.6.1/` 固定副本启动 WinAppCLI 安装终端。
- .NET SDK 完成后立即唤醒 WinUI 模板安装。
- 用户看到每个阶段的即时反馈，不等待全部下载结束才开始安装。

Developer Mode、管理员权限、测试证书信任、Microsoft 登录和 Store 提交属于人工确认点；这些内容不放入下载批次，也不把账号、Token、私钥或 PFX 放入仓库。

### Agent 的初始化行为约束

- 初始化阶段只处理工作站工具链，工具链验证完成后再开始产品访谈。
- 版本和下载地址以工具链锁定清单为唯一来源；README 只描述流程和用户可见状态。
- Agent 启动 3 个独立下载终端；Git worker 优先完成并安装，.NET 和 WinUI 下载终端同时运行。
- Agent 使用 Git-first 安装门槛；Git 完成后启动独立 clone 和 .NET SDK 终端，clone 完成后启动仓库内 WinAppCLI 终端，.NET 完成后启动独立 WinUI 模板安装终端，再进入 Skill handoff。
- Agent 不设置“等待所有下载结束再安装”的总闸门；总闸门只用于工具链状态和 Skill handoff 报告验收。
- 安装后以真实命令输出作为成功依据，不以 winget 的单一状态作为依据。
- Git 安装后立即刷新 PATH，或使用已安装工具的绝对路径继续执行 clone。
- 每一步都写入 `build/bootstrap-report.md`，包括资源 URL、版本、大小、下载开始/结束时间、安装开始/结束时间、依赖等待状态、命令、退出码和结果。

多线程下载、Git 优先和安装顺序必须能够从报告中验证。每项资源至少记录：

```text
artifact_id
download_start
download_end
download_ready
install_start
install_end
status
waiting_for
worker_id
queue_position
terminal_pid
terminal_kind
```

只列出四条下载命令或只汇总最终速度，仍不足以构成并行证据；报告必须展示时间重叠和单项安装启动时间。

### 初始化完成条件

只有同时满足以下条件，Agent 才进入 App 需求发现：

- Git 可执行并显示锁定版本。
- .NET SDK 可执行并显示锁定版本。
- WinUI 模板已注册，`dotnet new list winui` 可看到目标模板。
- NuGet 源已配置，模板包和锁定的来源信息可用；完整 restore 延后到真实 App 项目。
- WinAppCLI 可执行并显示锁定版本。
- `build/bootstrap-report.md` 已写入完整结果，并标记 `bootstrap_status: ready`。
- 仓库已 clone 到工作区，当前工作区没有初始化用的 Hello World 项目。
- 报告标记 `skill_handoff: ready`，下一步是开始需求发现。

Bootstrap 完成后立即交接 Skill：

```text
读取 skills/vainreef-fast-publish/SKILL.md
→ 使用 discovery-interview.md 的第一问
→ 向用户提问：
  “你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。”
```

初始化阶段保持工具链职责边界：不创建 `apps/hello-world/`，不生成临时产品工程，不执行默认 `dotnet run`，不提前进入 `dotnet publish`、MSIX 或 Store 流程。首次真实 App 的工程目录、living README、restore、build 和 run，都由用户给出第一段实质想法并确认产品方向后触发。

## 当前主线：Fast Publish V1

这个项目要验证的核心体验不是“AI 会生成一个页面”，而是：

> 普通人提出一个小需求，Agent 负责把它落成一个 Windows App；用户只在登录、身份确认、Store 表单和必要的管理员权限处回来操作。

Skill 收到新想法后先问“你想做什么”，让用户自由发挥创意。用户给出第一段实质想法后，Agent 立即在当前工作目录创建暂定项目文件夹和 living README；此后每轮先更新用户需要、目前省略、正在探索和已经确定的内容，同时检查新想法与固定路线的适配度。遇到完整 3D 游戏、实时多人、云账号、收费或深层系统访问等明显风险时，当轮就用通俗语言提醒并请用户确认方向。用户主动收口后，Agent 完整复述已经讨论和确认过的项目，得到确认后直接进入工程实现。

依赖能力通过 `skills/vainreef-fast-publish/capabilities/` Registry 管理，按内置能力、托管包、native wrapper、bundled CLI 和随 App 分发的 Python runtime 分级；外部运行时安装要求、服务器、隐藏项目方密钥、收费结算和深层系统访问会进入 Advanced Mode。用户看到简单的产品结论，详细版本、命令、日志与 hash 写入项目的 `build/run-report.md`。

### 首个演示 App：别纠结了

首个 Golden Path 采用一个小而完整的本地决策器：

- 输入或维护多个选项。
- 点击后随机给出一个决定。
- 支持多个列表、历史记录和本地保存。
- 首次打开就有示例数据。
- 主题与动画保持 Windows 11 风格。
- 数据留在本机，首版以离线体验为主。

它的价值在于镜头一眼看懂，同时足以覆盖完整链路：UI → 本地状态 → 存储 → 构建 → MSIX → Microsoft Store。

### Fast Mode 能力盒子

Fast Mode 的产品边界压缩为：**免费、本地、个人、通用**，再加上固定技术栈与 Microsoft Store only。它按服务器、账号、数据同步、开发者秘密、商业化、权限和隐私数据流判断实现路径，不按“聊天 App / 图片 App / 天气 App”这类产品名称做排除。

完整能力矩阵位于 `skills/vainreef-fast-publish/references/capability-boundary.md`：符合契约的需求直接实现；需求需要服务器、云同步、项目方 API key、收费或 Restricted Capability 时，先给出保留核心价值的本地投影，并标记 Advanced Mode。

## 我们现在先做什么

按下面顺序推进：先完成工具链 bootstrap，再交接 Skill 需求发现，最后验证真实 App 的发行链路：

1. **完成新 Windows 电脑 bootstrap**：多线程下载，Git 优先安装，立即 clone 仓库，随后按依赖和体积顺序完成工具链。
2. **交接 `vainreef-fast-publish`**：向用户提问 App 想法，创建 living README，逐轮完成需求发现和可行性判断。
3. **用户确认产品方向后创建真实 App**：从 Golden Template 生成项目，执行 restore、build、run 和修复循环。
4. **走通本地打包**：执行 `dotnet publish` 与 `winapp pack`，安装真实 App 测试包并重新启动。
5. **准备 Microsoft Store 账号与应用名**：完成 Partner Center 入口、身份验证、应用名预留和首个应用资料。
6. **提交一个最小可用测试 App**：用真实认证结果校准构建参数、清单、截图、年龄评级和审核时间。
7. **把成功路径固化进 `skills/vainreef-fast-publish`**：接入多线程 bootstrap、Skill handoff、Golden Template、自动检查和测试 Build 归档。

第一阶段的验收标准是：**同一台 Windows 机器上，从空项目到 Store 提交形成一条可重复路径。**

## V1 技术契约

Fast Publish Mode 先冻结底盘，再开放业务需求：

| 层 | V1 基线 |
| --- | --- |
| 目标平台 | Windows 11 优先；兼容范围以实机验证结果记录 |
| 语言 | C# |
| Runtime | .NET 10 LTS |
| UI | WinUI 3 + XAML |
| 平台层 | Windows App SDK Stable；具体版本由测试结果锁定 |
| 构建 | `dotnet` CLI |
| Windows 工具链 | `winapp` CLI；当前按 public preview 管理 |
| 包格式 | MSIX / Store submission package |
| 分发 | Microsoft Store |
| 小型本地数据 | `System.Text.Json` + 本地文件 |
| 结构化本地数据 | `Microsoft.Data.Sqlite`，按需求启用 |
| 第三方依赖 | Allowlist；每个新增包都记录理由与测试结果 |
| 架构 | XAML + code-behind + 简单 Service 层 |
| 后端 | Fast Mode 默认保持本地优先；网络按需求启用 |

底盘固定后，Agent 把自由度留给：产品名称、页面、交互、业务逻辑和数据模型。

## 仓库结构

```text
.
├── README.md
├── skills/                         # 可安装 Skill
├── toolchain/                      # 已锁定的工具链安装资源和校验文件
│   └── winapp-cli/0.6.1/
├── templates/
│   └── windows-golden-template/    # 经过验证的 WinUI 基础模板
├── apps/
│   └── test-builds/                # 测试 App 的构建产物与元数据
└── docs/
    └── research/                   # 研究记录与版本核对
```

### Skill 目录约定

每个 Skill 使用独立目录，核心文件为：

```text
skills/<skill-name>/
├── SKILL.md
├── agents/openai.yaml              # 需要出现在 Skill 列表时添加
├── scripts/                        # 可重复、适合脚本化的动作
├── references/                     # 按需加载的资料
└── assets/                         # 模板、图标与其他资源
```

Skill 名称使用小写字母、数字和连字符。Skill 主体保持精简，具体版本表、Store 字段和平台细节放进 `references/`。

新建 Skill 时，沿用 Codex Skill Creator 的初始化与校验流程：

```bash
python3 "$HOME/.codex/skills/.system/skill-creator/scripts/init_skill.py" \
  <skill-name> \
  --path ./skills \
  --resources scripts,references,assets

python3 "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" \
  ./skills/<skill-name>
```

### 测试 Build 目录约定

每个 App 使用一个版本目录，推荐格式：

```text
apps/test-builds/<app-slug>/<version>/
├── *.msix 或 *.msixbundle
├── build-info.json
├── SHA256SUMS.txt
└── screenshots/                    # 可选
```

`build-info.json` 至少记录：App 名称、版本、架构、源代码 commit、构建时间、.NET 版本、Windows App SDK 版本、`winapp` 版本和测试结果。开发证书、Token、私钥和本机配置留在机器上，归档目录只放可复现所需的公开信息。

## 研究结论（2026-08-18）

- 微软当前把 **WinUI 3** 定位为新 Windows 桌面应用的推荐原生 UI 框架，支持 C#/XAML，并覆盖 Windows 10 1809+ 与 Windows 11。
- **.NET 10** 当前为 LTS，官方支持到 2028-11-14；Skill 只固定主版本，补丁版本跟随经过测试的工具链。
- **`winapp` CLI** 覆盖 SDK、Package Identity、manifest、证书、MSIX 与 Store readiness，但官方文档当前标注为 public preview，因此版本和命令都要纳入验收矩阵。
- 微软已经提供从空目录、命令行、AI Agent 到 MSIX 和 Microsoft Store 的官方 Quickstart；路径可作为本项目的基线。
- 新的 Store 开发者账号流程显示注册费为 0，但仍需要 Microsoft 账号、身份验证、Partner Center 资料和人工确认点。
- Store 提交仍需要应用价值主张、完整可用体验、年龄评级；涉及个人信息时还要提供隐私政策 URL。首个 App 采用本地化设计，可以减少发布变量。

## 当前状态

- [x] 初始化 Git 仓库
- [x] 建立 Skill、模板和研究目录
- [x] 确定首个 Golden Path：本地决策器「别纠结了」
- [ ] 建立独立终端下载器和 Git-first 并行安装器
- [ ] 建立 bootstrap → Skill 需求发现交接
- [x] 下载并归档 WinAppCLI 0.6.1 x64 MSIX
- [ ] 建立测试 Build 归档目录
- [ ] 在 Windows 实机固定 Windows App SDK 与 `winapp` 版本
- [x] 创建 `skills/vainreef-fast-publish`
- [ ] 建出第一个可安装 MSIX
- [ ] 完成一次 Microsoft Store 提交并记录结果

## 官方资料

- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [Windows App Development CLI（winapp）](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/)
- [Quickstart: Build and publish a Windows app with AI](https://learn.microsoft.com/en-us/windows/apps/develop/ai-assisted/quickstart)
- [.NET Support Policy](https://dotnet.microsoft.com/platform/support/policy)
- [Open a Microsoft Store developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- [Publish your first Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app)
- [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
