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

## 1. Discover the app

用户还没有表达具体想法时，先问：

```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

用户给出第一段实质想法后：

1. 生成暂定 App 名称和小写连字符目录名。
2. 创建 `<app-slug>/README.md`。
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
docs/windows-smoke-test.md
```

规则：

- 版本信息读取 `bootstrap/toolchain.json`，避免在多份文档重复维护版本数字。
- 命令参数、退出码、日志位置和实测坑点读取 `commands.md`。
- 标记为“待实测”的命令先在当前 Windows 环境运行，以真实输出为准。
- 实测发现新的稳定规律时，补充命令、环境、错误原文和解决步骤。

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

**设计纪律（两轮实测：重复设计和过度工程是最大时间黑洞）：**

1. **设计只做一遍，写完决策就动手**：进入工程前把架构决策（存储方案、通知方案、模型字段、UI 结构）写进项目 README 的「已确定」一节，之后禁止在思考里从头重推整套设计。第二轮实测：同一套设计被完整重推 4 次（占日志 39%），决策反复 6+ 次，第一行代码在第 2527 行才落盘。
2. **API 行为不确定时禁止纯思考猜测**：任何 API（返回值类型、时限、平台限制）不确定，最多思考 1 个回合，然后必须查文档（webfetch 官方页面）或写最小实验验证。第二轮实测：ScheduledToast 时限猜 6 轮、MicaBackdrop 平台要求猜 11 轮、崩溃原因纯猜 12 回合，全部不如一次实测/一次 StartupProbe 日志。
3. **先最小闭环再叠加功能（MVP-first）**：第一版先跑通「启动 → 数据存取 → 渲染 → 打包安装」最小闭环，再逐个叠加通知等特性。不要为"可能不会发生的场景"预设预案：unpackaged 运行（本应用永远打包运行）、重复 toast 恐惧、闰年边界、通知 4096 上限——这些是第二轮超卖的三层通知状态机、双路径存储 fallback（死代码）、now+1 分钟臆想弹窗的根源。
4. **调试脚手架完工即拆**：StartupProbe、路径自报日志、双路径 fallback 这类为排障加的代码，问题定位后必须删除或移出产品路径，禁止"留着以后有用"（第二轮实测探针进了产品，每次启动写 7+ 行日志）。开发期验证代码与产品代码分离。
5. **catch 必须留痕**：禁止空 catch。至少写一条日志（开发期用 StartupProbe/%TEMP% 日志），否则"功能静默失效"无法排查（第二轮：toast 调度静默失败，排查半天）。
6. **改动模板结构时全局搜索孤儿 code-behind 调用**：删 XAML 控件时同步删 code-behind 里的对应调用（0x8007139F 教训）。

**执行命令的硬规则（两轮实测，违反必卡死）：**

1. **禁止把 `dotnet run` 挂在后台等待**：`dotnet run` 启动打包应用后 dotnet 进程一直存活，工具调用会因进程树/管道不退出而挂起 10 分钟以上，timeout 不生效，只能用户手动打断（两轮已发生 7 次）。实测细节（别被表象骗了）：
   - **`-PassThru` 没有保护作用**（7 次卡死里 3 次带了它照样卡）；
   - **`-RedirectStandardOutput/Error` 到文件也防不住**（7 次全部重定向仍卡）——整棵进程树继承工具捕获管道句柄；
   - **只在命令开头杀 app、不杀 dotnet 照样卡**；必须命令结尾把 `<AppName>` 和 `dotnet` 都杀净；
   - **判别律**：命令不返回 = 应用进程还活着（或其残留）；命令正常返回 ≠ 应用正常——返回快可能是应用秒崩，必须 `Get-Process` + `Get-WinEvent` 双验证；
   - **中断残留清理**：工具被手动打断后，残留的 dotnet run + app 进程树会污染下一条命令（实测一次纯 `dotnet build` 也因此卡死）。被打断后，下一条命令开头先 `Get-Process -Name <AppName>,dotnet -ErrorAction SilentlyContinue | Stop-Process -Force` 清残留；
   - 正确姿势：
     - 若用 `Start-Process dotnet "run"`，命令**结尾必须**杀干净 `<AppName>` 进程和 dotnet 进程再返回（先 app 后 dotnet，杀完验证两个都无输出）；
     - 或不用 dotnet run，改 `explorer.exe "shell:AppsFolder\<PFN>!App"` 启动已注册应用（实测切换后零复发）；
     - 或直接启动构建产物 exe 验证进程存活。
2. **每个调用 dotnet 的命令必须显式传 `workdir` 参数**（绝对路径字面量，不展开 `$env:` 变量）。否则 `dotnet build` 跑在默认目录报 MSB1003。
3. **大段脚本先写 `.ps1` 文件再执行**；长文件内容拆分写入。内联 here-string 和长 JSON 参数会被截断。
4. **不要用截图验证 UI**：多数模型无视觉能力，截图是死路。用 `winapp ui search/inspect/invoke/set-value` 做 UI 自动化验证。
5. **崩溃定位第一手段是 StartupProbe**：新建 `[ModuleInitializer]` 探针从 .NET 最早期入口逐阶段打点日志（module init → App ctor → InitializeComponent → OnLaunched → MainWindow → Activated），一次运行拿到精确堆栈。不要靠 UnhandledException（原生 stowed exception 0xc000027b 捕获不到）。
6. **测试数据要写对位置**：打包应用数据在 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`，且每次 `dotnet run` 重注册调试身份会清空它；持久化验证放在真实安装后做。中文测试数据用 JSON Unicode 转义。
7. **通知链路不要用空 catch**：静默吞异常会导致"没提醒"时无日志可查。本开发机是管理员会话，toast 永远弹不出来（系统限制），只能验证调度逻辑不报错。
8. **改动模板结构时全局搜索孤儿 code-behind 调用**：删 TitleBar XAML 却残留 `AppWindow.TitleBar.PreferredHeightOption = Tall` 会启动即崩（0x8007139F）。

完整细节和所有坑点见 `references/toolchain/v1/commands.md`「命令执行硬规则」和「Confirmed Windows findings」。

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

## 10. Sync findings back to the repo（每轮必做，在实机上完成）

实机上每一轮结束前必须执行：

```powershell
git add -A
git commit -m "docs: record Windows findings from <app-slug> round"
git push origin main
```

推回的内容包括：

- `references/toolchain/v1/commands.md` 的新增坑点（按 smoke-test 第 10 节的格式）。
- `docs/windows-smoke-test.md` 的实测记录表。
- `apps/<slug>/session-<id>.md` 会话日志和 `build/run-report.md`（可选，但建议）。

**两轮实测教训（必须内化）：** 第一轮经验没推回仓库、第二轮基于旧基线重踩了 0x80073CFB、两轮之间零提交——知识闭环连续三次断裂，第三轮开跑前必须确认仓库已是最新（`git pull`），结束后必须 push。会话日志只归档到本机不算完成，知识落进 `commands.md` 并 push 才算完成闭环。若检测到 `commands.md` 有未提交的本地分叉（本机 vs 仓库），先合并再开发。

## References

- [discovery-interview.md](references/discovery-interview.md)：渐进式需求访谈建议。
- [toolchain/README.md](references/toolchain/README.md)：工具版本与命令资料的职责。
- [toolchain/v1/commands.md](references/toolchain/v1/commands.md)：当前 Windows 命令和实测记录。
- [capabilities/registry.md](capabilities/registry.md)：可选能力建议与历史经验。
- [delivery-considerations.md](references/delivery-considerations.md)：联网、权限、发布和规模方面的设计提示。
- [official-sources.md](references/official-sources.md)：官方文档入口。
- [project-readme-template.md](assets/project-readme-template.md)：每个 App 的 living README 起点。
