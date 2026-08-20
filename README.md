# Vainreef Quick App Maker

> 用一个可复用的 Skill，把“说一句话做 App”推进到“生成、测试、打包、提交 Microsoft Store”。

这个仓库是 **Skill 工作台 + Windows 测试 Build 仓库**：

- `skills/`：存放可安装、可复用的 Codex/AI Agent Skill。
- `templates/`：存放 Golden Template 候选、经过验证的模板与相关资源。
- `apps/test-builds/`：预留每个测试 App 的 MSIX、校验信息与构建记录归档位置。
- `docs/research/`：存放平台、工具链与发布流程的研究记录。

## 新 Windows 电脑初始化：Agent 必须先走这里

本节是仓库级前置流程，优先级高于产品创意访谈和 App 生成功能。用户把仓库地址交给 Agent 后，Agent 先读取本节，准备开发工具；工具链完成验证后，再进入 App 需求发现。

初始化目标是：**先并行下载所有必需资源，等待下载批次全部完成并校验通过，再统一进入安装批次，最后一次性完成环境验证。**

当前初始化模式：`download-all-then-install`

### 初始化顺序

```text
读取 README
→ 读取工具链下载清单
→ 检查 Windows、架构、磁盘空间和已有版本
→ 并行下载所有必需资源
→ 等待所有下载结束
→ 校验文件存在、大小、SHA-256 和签名信息
→ 按依赖顺序连续安装，不在安装过程中重新下载
→ 验证 Git、.NET、WinUI 模板、NuGet 和 winapp
→ 写入 bootstrap 报告
→ 进入 App 需求发现
```

### 第一阶段：下载批次

初始化时先创建本地缓存目录，例如：

```text
<workspace>/build/bootstrap-cache/<toolchain-release>/
```

下载批次至少包含以下资源：

| 资源 | 用途 | 当前状态 |
| --- | --- | --- |
| Git for Windows x64 | 克隆、更新和提交仓库 | 已有国内直链 |
| .NET 10 SDK x64 | 创建和编译 WinUI 项目 | 等待国内直链 |
| WinUI C# template `.nupkg` | `dotnet new winui-navview` 和 Golden Template 维护 | 等待 NuGet 源或本地包 |
| WinAppCLI x64 | 运行、打包、证书和 MSIX | 等待国内直链 |
| Golden Template 依赖包 | Windows App SDK、BuildTools 和项目还原 | 等待 NuGet 源或本地 feed |
| WinApp/Windows App SDK 缓存 | 首次运行和打包所需的额外资源 | 按实机验证结果纳入 |

当前已知的 Git 下载入口：

```text
https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/Git-2.47.1-64-bit.exe
```

其余资源的地址、版本、文件名、大小和 SHA-256 进入工具链锁定清单；地址尚未填入前，保持为待配置项，不由 Agent 临时猜测包名或自行切换版本。

下载规则：

1. 所有资源先写入 `.part` 临时文件，下载完成后再改为正式文件名。
2. 所有资源并行下载；每项单独记录开始时间、进度、速度、结果和错误信息。
3. 主下载源、备用下载源和官方源只针对同一个固定版本，不跨版本替换。
4. 下载批次全部完成后，统一检查文件大小、SHA-256 和可用的 Authenticode 签名信息。
5. 某一项下载或校验失败时，保留缓存和报告，安装批次保持等待状态。

### 第二阶段：安装批次

下载批次全部完成后，才进入安装。安装阶段使用本地缓存文件，安装过程不再发起新的网络下载。

安装顺序固定为：

1. Git for Windows；安装后刷新当前 PowerShell 进程的 PATH，并用 `git --version` 验证。
2. .NET 10 SDK；使用 `dotnet --list-sdks` 和 SDK 目录双重验证，避免只相信 winget 的注册状态。
3. WinUI C# template；从已校验的本地 `.nupkg` 安装，并用 `dotnet new list winui` 验证。
4. NuGet 源和 Golden Template 依赖；优先使用本地 feed，再使用锁定的国内 NuGet 源。
5. WinAppCLI；使用固定的 x64 包安装或解压，并用 `winapp --version` 验证。
6. 执行 Hello World 的 restore、build 和 run smoke test。

Developer Mode、管理员权限、测试证书信任、Microsoft 登录和 Store 提交属于人工确认点；这些内容不放入下载批次，也不把账号、Token、私钥或 PFX 放入仓库。

### Agent 的初始化行为约束

- 初始化阶段只处理工作站工具链，工具链验证完成后再开始产品访谈。
- 版本和下载地址以工具链锁定清单为唯一来源；README 只描述流程和用户可见状态。
- Agent 先完成下载批次，再开始安装批次；下载和安装不交错进行。
- 安装后以真实命令输出作为成功依据，不以 winget 的单一状态作为依据。
- Git 安装后立即刷新 PATH，或使用已安装工具的绝对路径继续执行 clone。
- 每一步都写入 `build/bootstrap-report.md`，包括资源 URL、版本、大小、SHA-256、命令、退出码和验证结果。

### 初始化完成条件

只有同时满足以下条件，Agent 才进入 App 需求发现：

- Git 可执行并显示锁定版本。
- .NET SDK 可执行并显示锁定版本。
- WinUI 模板已注册，`dotnet new list winui` 可看到目标模板。
- NuGet 源可还原 Golden Template 的固定依赖。
- WinAppCLI 可执行并显示锁定版本。
- Hello World 已完成 restore、build 和 run smoke test。
- `build/bootstrap-report.md` 已写入完整结果。

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

按下面顺序推进，先完成新 Windows 电脑初始化，再验证发行链路，最后扩展 Skill：

1. **完成新 Windows 电脑初始化**：按本 README 先并行下载，再统一安装和验证。
2. **走通最小 Hello World**：创建 WinUI 项目，执行 `dotnet restore`、`dotnet build` 和 `dotnet run`，确认 Agent 能完成生成、运行、修复循环。
3. **走通本地打包**：执行 `dotnet publish` 与 `winapp pack`，安装测试包并重新启动。
4. **准备 Microsoft Store 账号与应用名**：完成 Partner Center 入口、身份验证、应用名预留和首个应用资料。
5. **提交一个最小可用测试 App**：用真实认证结果校准构建参数、清单、截图、年龄评级和审核时间。
6. **把成功路径固化进 `skills/vainreef-fast-publish`**：接入 Golden Template、下载批次、安装批次、自动检查和测试 Build 归档。

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
- [ ] 建立新 Windows 电脑的并行下载清单
- [ ] 建立统一安装和 bootstrap 报告流程
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
