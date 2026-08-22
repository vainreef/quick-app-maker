---
name: vainreef-fast-publish
description: "Turn a natural-language Windows app idea into a working WinUI project, then build, run, package, and prepare it for Microsoft Store submission. Use the repository bootstrap for the Windows toolchain, use the versioned command notes for known Windows behavior, let the agent design and write each app from its requirements, and feed newly observed build problems back into the command notes."
---

# Vainreef Fast Publish

这个 Skill 给 Agent 一条可靠的 Windows App 工作路径。仓库负责准备工具、保存实测命令、记录 Windows 坑点和定义交付步骤；每个 App 的页面、代码、目录结构、数据模型和依赖选择由 Agent 根据需求现场设计。

## Core idea

仓库提供的是骨架与经验，不是预写好的 App 源码模板。

1. `bootstrap/toolchain.json` 是 Bootstrap 安装版本与下载地址的唯一来源。
2. `references/toolchain/<release>/commands.md` 保存对应工具链经过 Windows 实测的命令、输出和坑点。
3. Agent 从官方 WinUI CLI 模板创建一个最小工程，然后按当前 App 需求写代码。
4. 项目架构、NuGet、native library、CLI、数据存储和页面组织由 Agent 判断。
5. Capability 资料只给建议和历史经验，不承担准入职责。
6. 先执行最直接的构建路径；出现错误后读日志、检查环境、检查缓存并修复。
7. 新发现的 Windows 行为经过复现后，再写回 `commands.md`。

## 对用户的语言规则

用户只理解日常语言。Agent 说给用户听的每一句话都不得出现内部术语或工程名词，包括但不限于：Fast Mode、契约、Skill、模板、draft、Golden Template、技术栈、框架名、编程语言、SDK、MSIX、权限名、包名、工具名、命令行、版本号、项目目录名。内部判断只发生在 Agent 思考中，不进入用户可见文字。需要解释工程影响时，用“做起来会……”“需要系统允许通知”“会占一点网络”这类日常表达。这条规则覆盖访谈、构建、打包和上架指引的全程，不只限于需求阶段。

Agent 环境里用户能看到的任何文字都算“对用户可见”，包括过程说明、进度更新和可行性判断。可行性分析、边界检查和版本核对一律放在隐藏思考中；可见输出只保留：面向用户的问题、面向用户的复述、面向用户的指引，以及必要的安装/构建进度（用日常语言）。开场白禁止出现“契约”“边界”“可行”“落地”这类内部口吻。

## Toolchain skeleton

Bootstrap 当前准备：

| 项目 | 默认工具 |
| --- | --- |
| 平台 | Windows x64 |
| Runtime | .NET 10 SDK |
| UI 起点 | WinUI 3 C# template |
| 构建 | `dotnet` CLI |
| 包工具 | `winapp` CLI |
| 默认包格式 | MSIX |
| 默认发布目标 | Microsoft Store |

这张表描述仓库已经准备好的路径。具体 App 需要其他架构或依赖时，Agent 根据产品目标选择，并在项目 README 和运行报告里记录理由与验证结果。

## 工作目录规则（绝对禁止违反）

```text
Agent 工作根目录（就是仓库 clone 所在的父目录）
├── quick-app-maker/     ← clone 下来的仓库：只读 skill，Agent 不改、不 commit、不 push
├── <app-slug>/          ← 每个 App 的项目目录（与仓库同级！）
├── 其他 app/
└── 临时测试、下载、脚本等   ← 也放这里
```

1. **所有文件操作都在 Agent 工作根目录内**：新项目、README、临时测试、安装包、脚本，全部放在这个总文件夹里、仓库目录同级或之下。**禁止往其他盘符/系统目录/用户目录写任何东西**（如 `C:\Users\<user>\Developer\...`、`C:\temp`、桌面等）——那样会导致权限弹窗、文件找不到，且违反用户明确规则。
2. **仓库目录只读（含行尾符保护）**：clone 下来的 `quick-app-maker/` 对 Agent 是 skill 参考，**不修改其内容、不 git add/commit/push**。修改仓库内容由外部主控（Mac 端）负责。**读取仓库文件时禁止用会改写行尾符（CRLF/LF）或加 BOM 的编辑器/命令**——实测多次因读取动作把仓库文件从 LF 改成 CRLF，`git status` 出现大量假 modified。发现行尾符噪音时：`git -C <repo> checkout -- .` 还原即可，不要手动"修复"。
3. **禁止任何 git 推送**：Agent 不需要、也没有权限 push。git 操作最多是 `git pull` 获取最新 skill 知识；push/commit 一律不做（需要凭据，会弹窗打断用户）。
4. **验证数据落盘不要直接读打包应用的 LocalState**：`C:\Users\<user>\AppData\Local\Packages\<PFN>\LocalState\` 在 Agent 工作根目录之外且受 MSIX 保护，直接 `Get-Content` 会触发权限确认框（实测第四轮踩过）。**正确做法**：用 `winapp ui search` 查 UI 是否显示新数据；或让应用把状态日志写到工作根目录内（如 `<app-slug>/logs/`）；或通过应用自身导出/打印。任何需要读 `C:\Users\...` 路径的操作先想替代方案，不要直接读。
5. 工作根目录的判定：`quick-app-maker` 仓库目录的父目录即是工作根目录。

## 1. Discover the app

用户还没有表达具体想法时，先问：

```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

用户给出第一段实质想法后：

1. 生成暂定 App 名称和小写连字符目录名。
2. 在工作根目录下创建 `<app-slug>/README.md`（与 `quick-app-maker/` 同级，见上方工作目录规则）。**严禁在仓库目录里创建 App 内容，严禁使用任何 `~/...` 或 `C:\Users\...` 之类的绝对路径。**
3. 以 [project-readme-template.md](assets/project-readme-template.md) 为起点记录需求。
4. 每轮更新当前想法、用户需要、暂时省略、交互流程、风格、待明确项和用户决定。
5. 每轮只问一个真正会影响产品体验的问题。

完整访谈建议见 [discovery-interview.md](references/discovery-interview.md)。

用户表达“按这个来”“开始吧”“第一版就这样”等收口信号时：

1. 更新 README。
2. 用日常语言复述 App 的完整体验。
3. 结尾询问：`我理解的是这样，对吗？`
4. 用户确认后进入工程阶段。

## 2. Read the Windows build notes

开始工程前读取：

```text
bootstrap/toolchain.json
references/toolchain/README.md
references/toolchain/v1/commands.md
references/test-assets.md
docs/windows-smoke-test.md
```

规则：

- 版本信息读取 `bootstrap/toolchain.json`，避免在多份文档重复维护版本数字。
- 命令参数、退出码、日志位置和实测坑点读取 `commands.md`。
- 标记为“待实测”的命令先在当前 Windows 环境运行，以真实输出为准。
- 实测发现新的稳定规律时，补充命令、环境、错误原文和解决步骤。
- **每轮开始前先 `git pull --ff-only`** 获取最新 skill 知识（只读操作，允许）；若 `commands.md` 有外部主控刚合并的新内容，以仓库为准。

## 3. Create the project

仓库里没有 Golden Template。工程从当前工具链提供的官方模板生成：

```powershell
dotnet new winui-navview -n APP_NAME
Set-Location APP_NAME
```

创建后，Agent 先读取实际生成的：

- `.csproj`
- `App.xaml` 与 code-behind
- 主窗口与页面
- `Package.appxmanifest`
- 资源和打包配置

然后根据 App 需求调整。官方模板未来更新后，Agent 以当前生成结果为准。

## 4. Let the agent design the app

**设计纪律先看（两轮实测：重复设计和过度工程是最大时间黑洞）：**

1. **设计只做一遍，写完决策就动手**：把架构决策（存储方案、通知方案、模型字段、UI 结构）写进项目 README 的「已确定」一节，然后直接进入编码。禁止在思考里从头重推整套设计（第二轮同一套设计被完整重推 4 次、占日志 39%，第一行代码在第 2527 行才落盘）。
2. **API 行为不确定时禁止纯思考猜测**：最多思考 1 个回合，然后查官方文档或写最小实验验证。不要靠回忆猜（ScheduledToast 时限猜 6 轮、MicaBackdrop 平台要求猜 11 轮，全不如一次实测）。
3. **先最小闭环再叠加功能（MVP-first）**：第一版先跑通「启动 → 数据存取 → 渲染 → 打包安装」最小闭环，再逐个叠加通知等特性。不要为"可能不会发生的场景"预设预案（unpackaged 运行、重复 toast、闰年边界——第二轮三层通知状态机、双路径存储 fallback 都是这类臆想预案）。
4. **调试脚手架完工即拆**：StartupProbe、路径自报日志、双路径 fallback 为排障加的代码，问题定位后必须删除或移出产品路径（第二轮探针进产品，每次启动写 7+ 行日志）。
5. **catch 必须留痕**：禁止空 catch，至少写一条日志。否则"功能静默失效"无法排查。

设计阶段如果 App 需要图片/图标/音频/字体等素材（卡片配图、空状态插画、通知音、标题字体……），先读 [test-assets.md](references/test-assets.md) 看获取方式，把"用什么素材、从哪拿"写进 README 的「已确定」一节——不要在编码时才临时找。

Agent 根据当前需求自由选择：

- 单页、多页、导航、窗口数量。
- code-behind、MVVM、DI 或更简单的结构。
- JSON、SQLite、文件夹、数据库或网络服务。
- .NET 包、native-backed 包、bundled CLI 或其他能力实现。
- 同步、异步、缓存、后台任务和错误恢复方式。

选择原则是让当前 App 容易理解、容易运行、容易打包。复杂度服务于产品需求，不由仓库提前规定。

涉及额外依赖时，至少在 `build/run-report.md` 记录：

- 包名和版本。
- 用途。
- 是否包含 native 文件或额外进程。
- 在当前 x64/MSIX 构建中的结果。
- 许可证或随包说明事项。

可选经验见 [capabilities/registry.md](capabilities/registry.md)。条目缺失时，Agent 直接研究和试验适合当前 App 的实现。

## 5. Build in short loops

推荐循环：

```text
实现一个完整小步骤
→ dotnet build
→ 处理当前错误
→ 验证运行（见下方硬规则）
→ 更新 README / run-report
→ 继续下一步
```

**设计纪律：见第 4 节（设计只做一遍、API 不确定查文档/最小实验、MVP-first、调试脚手架完工即拆、catch 留痕、孤儿 code-behind 检查）。** 编码过程中任何违反这些纪律的冲动，先停下来回到第 4 节重读。

**素材获取（需要图标/图片/音频/字体/测试数据/动画/3D/异常文件时）：**

1. **遇到任何素材需求，先查 [test-assets.md](references/test-assets.md)**——里面所有命令都经过实测，直接复制即可。不要自己搜网站、不要自己造轮子、不要用不存在的工具（node/python/ffmpeg 都没有）。
2. **第一条原则：能本地生成就别下载**（PowerShell + System.Drawing 画 PNG、写假数据、复制截断造损坏文件），覆盖大部分测试场景。
3. 需要"真实内容"（照片/图标/字体/声音）才下载，且只走 test-assets.md 里的两条路：curl 直链（无 Key）或 git clone。
4. 素材文件一律放在工作根目录内的项目目录下（如 `<app-slug>/Assets/`、`<app-slug>/testdata/`），不写工作区外。

**执行命令的硬规则：见 `references/toolchain/v1/commands.md`「命令执行硬规则」——这是权威完整版，每条都经过实机验证。** 最重要的一条：禁止把 `dotnet run` 挂在后台等待（会卡死整个会话，已发生 7 次）。每次执行命令前先对照该清单，遇到问题再回来查「Confirmed Windows findings」的 32 条坑点。

遇到问题时依次查看：

1. 当前命令的 stdout、stderr 和退出码。
2. 实际 Windows、.NET、WinUI 模板和 `winapp` 版本。
3. 当前 `.csproj`、manifest 和生成目录。
4. `references/toolchain/v1/commands.md` 已记录的实测经验。
5. 缓存文件、安装包或 nupkg 是否在失败环节表现异常。

正常路径直接执行。安装或包读取失败后，再记录文件大小与 SHA-256，清理对应缓存并重新下载一次。Bootstrap 对外部安装包采用这一失败后诊断策略。

## 6. Smoke test

工具链、命令或项目生成方式发生变化时，执行 [windows-smoke-test.md](../../docs/windows-smoke-test.md)。

最小 Smoke Test 覆盖：

1. Bootstrap 完成。
2. 创建一次性 WinUI App。
3. Debug/Release 构建。
4. 启动并关闭 App。
5. Publish。
6. 使用当前 `winapp` 命令打包。
7. 安装、启动、卸载和重新安装 MSIX。

每次测试记录 Windows build、工具版本、命令、退出码、输出路径和结果。实际踩到的稳定坑点写回 `commands.md`。

## 7. Validate the real app

按项目 README 的验收标准逐项验证：

- 首次启动。
- 核心用户流程。
- 空状态与错误状态。
- 重复启动与关闭重开。
- 本地数据重新加载。
- 网络或文件失败场景。
- Debug 与 Release 构建。
- 包身份、显示名称、图标、版本和架构。

Agent 根据当前 App 增加针对性测试。

## 8. Package and publish

打包命令读取当前 release 的 `commands.md`。本地测试证书、MSIX 安装和 Store 包分开记录。

发布前核对：

- App manifest 与 Store 资料一致。
- Display Name、Publisher、版本和包身份一致。
- 图标、截图、描述和年龄评级材料齐全。
- App 使用的数据、网络和系统能力已经反映在 manifest 与 Store 说明中。
- `APP_ID`、市场、价格、可见性和最终提交由用户确认。

登录、Partner Center 表单和最终提交属于用户确认点；代码生成、构建、检查、打包和技术报告由 Agent 继续推进。

## 9. Technical run report

每个 App 在 `build/run-report.md` 记录：

- 需求摘要与验收标准。
- 项目目录和主要文件。
- Windows、.NET、模板和 `winapp` 版本。
- 实际执行命令、stdout/stderr 摘要与退出码。
- Debug、Release、Publish、MSIX 和安装结果。
- 依赖、native 文件、子进程与许可证说明。
- 最终包路径、架构、版本和 SHA-256。
- 新发现的 Windows 坑点及复现条件。
- Store 人工确认项。

## 10. Record findings locally（Agent 只记录，不 push）

**Agent 不做任何 git 写操作**（add/commit/push 全部禁止，需要凭据会弹窗打断用户，且仓库是只读 skill）。

每轮结束后，Agent 在**工作根目录**里写一份本轮技术记录：

```text
<工作根目录>/round-notes/round-N.md
```

内容包括（只写**通用技术经验**，不写具体 App 名/源码/会话内容）：

- 新发现的 Windows 坑点（按 commands.md 的格式：Environment / Command / Error text / Root cause / Fix / Retest result）。
- 新验证可行的命令、参数组合和退出码。
- 设计/流程层面的教训。

**修改仓库文档（commands.md / SKILL.md / windows-smoke-test.md）由外部主控负责**：主控会读取 `round-notes/` 和会话日志，把通用经验合并进仓库文档并推送。Agent 不要自己改仓库内容，也不要 commit/push。

两轮实测教训：实机经验此前要么没记录、要么直接改仓库却因无凭据 push 失败（弹窗打断用户）。正确路径：Agent 记录到 `round-notes/`，主控合并回仓库。

## References

- [discovery-interview.md](references/discovery-interview.md)：渐进式需求访谈建议。
- [toolchain/README.md](references/toolchain/README.md)：工具版本与命令资料的职责。
- [toolchain/v1/commands.md](references/toolchain/v1/commands.md)：当前 Windows 命令和实测记录。
- [capabilities/registry.md](capabilities/registry.md)：可选能力建议与历史经验。
- [delivery-considerations.md](references/delivery-considerations.md)：联网、权限、发布和规模方面的设计提示。
- [test-assets.md](references/test-assets.md)：测试素材来源清单（图标/图片/音频/字体/数据，含许可红线）。
- [official-sources.md](references/official-sources.md)：官方文档入口。
- [project-readme-template.md](assets/project-readme-template.md)：每个 App 的 living README 起点。
