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

## 对用户的语言与心智规则

1. **绝对白话与零黑话**：用户只理解日常语言。Agent 说给用户听的每一句话都不得出现内部术语或工程名词，包括但不限于：Fast Mode、契约、Skill、模板、draft、Golden Template、技术栈、框架名、编程语言、SDK、MSIX、权限名、包名、工具名、命令行、版本号、项目目录名。内部判断只发生在 Agent 思考中，不进入用户可见文字。需要解释工程影响时，用“做起来会……”“需要系统允许通知”“会占一点网络”这类日常表达。
2. **建立内部环境降噪屏障（极其重要）**：开发机测试沙箱的特例（如管理员提权会话不弹 Toast 通知、调试证书信任、进程虚拟化重定向等）属于纯内部调试信息，**绝对不得当作借口倒给用户或向用户解释技术原理**。面向用户时，只清晰描绘产品在日常使用中的真实表现（例如：“提前一天的提醒已经排上了，平时用的时候前一天上午会弹通知提醒你，只要在系统设置里给它开启通知权限即可”）。把复杂留给自己，把轻松与清爽留给用户。
3. **产品共创与把玩优先心智（拒绝迫不及待推销上架）**：
   - 第一版开发完成（安装到电脑上）不是终点，而是**邀请用户上手体验与共同打磨的起点**。
   - 交付第一版时，Agent 的本能偏好应当是：热情真诚地邀请用户打开试用（“已经装在你的电脑上了，随时可以打开试试！”），引导体验核心亮点，并主动索取改进反馈：“**你先体验一下，觉得哪里不好看、哪里不顺手、想改什么随时告诉我，我们继续打磨，直到你满意为止。**”
   - **“上架商店”永远是被动触发的终局里程碑**：只要用户还在提修改要求、还在体验，就绝不主动推销上架；只有当用户多次把玩、明确表示“太棒了，完全满意”或者主动询问“怎么发给朋友/怎么上架”时，才顺理成章承接上架指引。

Agent 环境里用户能看到的任何文字都算“对用户可见”，包括过程说明、进度更新和可行性判断。可行性分析、边界检查和版本核对一律放在隐藏思考中；可见输出只保留：面向用户的问题、面向用户的复述、面向用户的指引，以及必要的安装/构建进度（用日常语言）。开场白禁止出现“契约”“边界”“可行”“落地”这类内部口吻。

## Toolchain skeleton

Bootstrap 当前准备：

| 项目 | 默认工具 |
| --- | --- |
| 平台 | Windows x64 |
| VCS | MinGit 2.47.1 绿色免安装版 (npmmirror 镜像, 零 UAC 提权) |
| Runtime | .NET 10 SDK 官方 Binaries 绿色免安装版 (零 UAC 提权, 零写C盘) |
| UI 起点 | WinUI 3 C# template |
| 构建 | `dotnet` CLI (挂载工作区 DOTNET_ROOT) |
| 包工具 | `winapp` CLI |
| 默认包格式 | MSIX |
| 默认发布目标 | Microsoft Store |

这张表描述仓库已经准备好的路径。具体 App 需要其他架构或依赖时，Agent 根据产品目标选择，并在项目 README 和运行报告里记录理由与验证结果。

## 工作目录规则（绝对禁止违反）

```text
Agent 工作根目录（WORKSPACE_ROOT，例如 Project/）
├── git/                 ← MinGit 绿色免安装版（与仓库同级，零 UAC 提权）
├── dotnet/              ← .NET 10 SDK 绿色免安装版（与仓库同级，零写C盘）
├── quick-app-maker/     ← 核心工具链与 Skill 知识库（同级）
├── <app-slug>/          ← 每个 App 的项目目录（与仓库同级！）
├── 其他 app/
└── 临时测试、下载、脚本等   ← 也放这里
```

1. **所有文件读写与命令操作绝对限定在 Agent 工作根目录（WORKSPACE_ROOT）内**：新项目、README、临时测试、安装包、脚本、工具缓存（`.cache/`），全部必须在当前工作目录下新建文件夹或文件进行读写。**绝对禁止往系统临时目录（`$env:TEMP`、`/tmp`）、系统应用数据目录（`%LOCALAPPDATA%`）、系统盘根目录（`C:\temp`）或用户根目录（`C:\Users\<user>\...`）读写任何文件**——这会当场触发沙箱安全确认拦截或权限弹窗打断用户！
2. **验证数据落盘不要直接读打包应用的 LocalState**：`C:\Users\<user>\AppData\Local\Packages\<PFN>\LocalState\` 在 Agent 工作根目录之外且受 MSIX 保护，直接 `Get-Content` 会触发权限确认框（实测第四轮踩过）。**正确做法**：用 `winapp ui search` 查 UI 是否显示新数据；或让应用把状态日志写到工作根目录内（如 `<app-slug>/logs/`）；或通过应用自身导出/打印。任何需要读 `C:\Users\...` 路径的操作先想替代方案，不要直接读。
3. **工作根目录的判定**：`quick-app-maker` 仓库目录的父目录（或执行脚本时的当前工作目录）即是工作根目录。严禁跨出此目录读写任何外部文件。

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
5. **【双窗口并行机制】启动构建时向用户输出友好非阻塞提示**：
   ```text
   我已开始为你全自动编写代码、生成界面、打包并进行自动化测试（大约需要 15~20 分钟）。
   这段时间如果你想提前准备微软开发者账号，可以随时新开一个对话窗口向我提问：“我要如何创建 Partner 开发者账号？”，我会指引你完成免费个人认证。发布时的名称验重、建项与商店提审将由工具链全自动完成。
   ```
6. **构建主会话保持极致专注**：本会话专注于代码生成、编译、自包含 MSIX 打包、`winapp ui` 自动化黑盒测试与本地安装，绝不在同一会话里混杂交互问答，避免上下文污染与任务中断。

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
- **每轮开始前先用同级免安装版 MinGit 执行 `.\git\cmd\git.exe -C quick-app-maker pull --ff-only origin main`** 获取最新 skill 知识；若 `commands.md` 有新内容，以仓库为准。

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

### WinUI 3 一次性写对的 5 大黄金铁律（防假死报错、防运行时秒崩）

1. **【铁律 1】DataTemplate 必须显式声明 `x:DataType`（防 WMC9999 假资源错误）**：
   - 凡是在 `<DataTemplate>` 内部使用 `{x:Bind ...}`，根节点必须显式声明 `x:DataType="models:YourClass"`，且 XAML 根节点必须引入命名空间 `xmlns:models="using:YourApp.Models"`。
   - **避坑警示**：如果漏写，XamlCompiler 内部崩溃会报假死错误 `error WMC9999: 未能找到任何适合于指定的区域性... ErrorMessages.resources`。**看到 WMC9999 假错误，100% 是 DataTemplate 漏写了 x:DataType！绝对禁止改 csproj、降级依赖或换包！**
2. **【铁律 2】所有 ContentDialog 弹窗必须显式设置 `XamlRoot`（防 0xc000027b 启动秒崩）**：
   - WinUI 3 的 `ContentDialog` 必须依附在窗口视觉树上，打开前必须设置 `XamlRoot`：
     ```csharp
     var dialog = new CustomDialog { XamlRoot = this.Content.XamlRoot };
     await dialog.ShowAsync();
     ```
   - **避坑警示**：漏设 `XamlRoot` 直接 `ShowAsync()` 会当场触发底层原生 `0xc000027b` 闪退崩溃！
3. **【铁律 3】计划通知的标准调度范式（防 CS0117 与空消息异常）**：
   - `ScheduledToastNotification` 没有 `Recurrence` 属性（写了必报 CS0117）；每年重复提醒通过 `for` 循环为未来几年分别构造单次通知调度；
   - 使用 `Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().AddToSchedule(...)`；XML 特殊字符（`&`, `<`, `>`, `"`）必须转义；catch 时必须打印 `ex.HResult`（提权环境 0x803E0120 时 Message 为空）。
4. **【铁律 4】全生命周期零 UAC 提权 & 零 C 盘系统目录写入（免安装 MinGit + 免安装 .NET SDK + CurrentUser 证书 + 严禁写 HKLM）**：
   - **底层机制（坑 62）**：AI Runner 执行线程位于私有桌面 `WinSta0\exebox-*`，而用户在 `WinSta0\Default`，UAC 在 `WinSta0\Winlogon`。跨 Desktop 直接调用 `runas` 会被底层直接报 `0x80070032 (ERROR_NOT_SUPPORTED)` 拦截；
   - **工程铁律**：Git 与 .NET 10 SDK 全程使用工作区根目录下同级的 `git\` 与 `dotnet\` 绿色免安装版，绝不运行任何 EXE 安装器；证书只导入 `Cert:\CurrentUser\TrustedPeople`，严禁写入 `HKLM:` 或 `Cert:\LocalMachine\`，全生命周期在 Medium Integrity 下顺畅运行，杜绝触发提权。若极特殊场景确需提权，严禁关闭系统安全，必须使用 `lpDesktop="WinSta0\Default"` 桥接启动。
5. **【铁律 5】国内 NuGet 还原镜像源（防 restore 超时卡死）**：
   - 还原依赖必须指定国内源：`dotnet restore --source https://nuget.azure.cn/v3/index.json`，严禁直连境外 `api.nuget.org`。

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

1. **【绝对红线】严禁一切下载海外资源的尝试！开发环境在中国境内，直连海外公网必被墙、必超时卡死！** 严禁心存侥幸使用 curl/git 直接访问境外未镜像站点（如 GitHub releases, raw.githubusercontent, 海外 API/CDN 等）。
2. **遇到任何素材需求，先查 [test-assets.md](references/test-assets.md)**——里面所有命令都经过国内实机实测，直接复制即可。不要自己去公网盲搜，不要用不存在的工具（node/python/ffmpeg 默认都没有）。
3. **首选本地原生/系统自带，零网络最稳**：
   - 纯色/渐变/图标 PNG：用 PowerShell + `System.Drawing` 本地绘制；
   - 短音效/提示音/铃声：直接复制 Windows 自带 `C:\Windows\Media\*.wav`；
   - UI 图标与头像：直接用 XAML 内置 `Segoe Fluent Icons` 字体字形与 `PersonPicture` 控件；
   - 测试压缩包/数据：用 PowerShell 原生 `Compress-Archive` 与 `Set-Content` 本地生成。
4. **下载资源的铁律：必须使用明确的国内资源，搜不到国内资源就必须立刻放弃下载！**
   - 确实需要真实外部文件时，只走 `test-assets.md` 中记录的国内源（Gitee 镜像、清华 TUNA、阿里云 OSS、npmmirror、img.scdn.io 等）；
   - **如果某项资源找不到可用的国内源，必须彻底放弃下载该资源！绝对禁止尝试海外下载，必须换用本地生成、系统自带或 XAML 控件拟态等替代方案！**
5. 素材文件一律放在工作根目录内的项目目录下（如 `<app-slug>/Assets/`、`<app-slug>/testdata/`），不写工作区外。

**执行命令的硬规则：见 `references/toolchain/v1/commands.md`「命令执行硬规则」——这是权威完整版，每条都经过实机验证。** 最重要的一条：禁止把 `dotnet run` 挂在后台等待（会卡死整个会话，已发生 7 次）。每次执行命令前先对照该清单，遇到问题再回来查「Confirmed Windows findings」的 36 条坑点。

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

## 8. Deliver MVP, invite play, and co-create iterations

打包安装完成后，Agent 进入交付与共创循环：

1. **第一版交付话术规范**：
   - 告知用户应用已经安装在电脑上（可从开始菜单打开）；
   - 用 2~3 句日常语言介绍核心功能与使用方法；
   - 热情邀请用户试用，并明确表达共创意愿：“**你先打开体验一下，看哪里不好看、哪里不顺手、想改什么随时告诉我，我们继续修改，直到你满意为止。**”
2. **严禁在第一版主动推销上架**：
   - 绝不主动提“要不要看看上架的事”；
   - 用户的心理节奏是先玩、先改、改满意后才考虑分享与发布。
3. **用户反馈迭代循环**：
   - 接收用户体验反馈 → 更新 README（用户需要/调整） → 极简修改代码 → 本地构建与升级重装（递增 manifest Version） → 再次请用户体验。

## 9. Microsoft Partner Center onboarding and operation guide

本模块为 Agent 向用户提供微软开发者中心（Partner Center）全流程协助的标准规范。支持**独立咨询会话（窗口 2）**与**发布时刻智能断点续接**：

- **详细权威操作手册与架构白皮书**：严格查阅 [Edge-Store-全流程发布白皮书与实战手册](../../docs/partner-center/Edge-Store-全流程发布白皮书与实战手册.md)、[partner-center-guide.md](references/partner-center-guide.md) 及 `docs/partner-center/` 内的真实表单 DOM 快照。
- **声明式 Edge 自动化入口 (V2)**：先读取 [Agent 运行契约](../../docs/partner-center/Agent-运行契约.md) 和 [可靠性重构](../../docs/partner-center/Edge-Store-可靠性重构.md)，再使用唯一正式入口 `toolchain/edge-store-cli/Invoke-EdgeStore.ps1`。`apps/Project/edge-store-cli-fast` 只是一轮旧诊断副本，不执行其中的 DLL。大 SPA 禁止 `DOM.getDocument(depth:-1,pierce:true)`；采用浅根、局部 `DOM.requestNode`、组件 Shadow Root 与 Accessibility Tree 语义化定位，严禁硬编码绝对坐标。
- **独立咨询会话与自动化建项规范**：
  1. 当用户新开窗口提问“如何创建 Partner 账号 / 如何起名验重”时，Agent 专注提供咨询服务；
  2. 指引用户通过 Xbox 应用注册避开真人验证码异常，选择免费「个人开发者」并完成身份证照片上传；
  3. **全新产品全自动建项**：用户完成首次登录后，Agent 直接调用 `Invoke-EdgeStore.ps1 -Action reserve -AppName "..." -Manifest build/edge-store.json` 自动在 Partner Center 完成产品创建、名称可用性检查、预留名称并自动抓取 3 大 Identity 回填到 `Package.appxmanifest` 与 `edge-store.json`，严禁让用户手动执行控制台 5 步！
- **发布时刻的断点智能续接状态机（严禁单脚本一冲到底）**：
  - 当用户在构建主会话（窗口 1）试用满意并表达上架意向时，Agent 启动发布链条标准流水线，必须按独立离散阶段逐一推进与自检：
    ```text
    STORE -1: 自动建项验重 (Invoke-EdgeStore.ps1 -Action reserve 自动预留名称并提取 3 大 Identity)
    STORE 0: 离线静态质检 (Invoke-EdgeStore.ps1 -Action preflight -Manifest ...)
    STORE 1: 建立独立常驻 Edge 会话并在用户桌面确权 (Invoke-EdgeStore.ps1 -Action launch -KeepOpen)
    STORE 2: 动态 DOM 探测与基线快照 (Invoke-EdgeStore.ps1 -Action discover / inspect)
    STORE 3~8: 逐表离散单步步进与阶段后强制 DOM 自检：
      - availability: Invoke-EdgeStore.ps1 -Action step -Phase availability -Manifest ... -Apply
      - properties:   Invoke-EdgeStore.ps1 -Action step -Phase properties -Manifest ... -Apply
      - ageRatings:   Invoke-EdgeStore.ps1 -Action step -Phase ageRatings -Manifest ... -Apply
      - packages:     Invoke-EdgeStore.ps1 -Action step -Phase packages -Manifest ... -Apply
      - listing:      Invoke-EdgeStore.ps1 -Action step -Phase listing -Manifest ... -Apply
      - options:      Invoke-EdgeStore.ps1 -Action step -Phase options -Manifest ... -Apply
    STORE 9: 概览页 6 大模块冷加载全绿勾总检 (Invoke-EdgeStore.ps1 -Action verify)
    STORE 10: 显式双确认提交审核 (Invoke-EdgeStore.ps1 -Action submit -ConfirmSubmit)
    ```
  - **断点续接与 DOM 自检闭环**：每个阶段执行后，必须触发专属的 DOM 探针比对 DOM 真实值与 DesiredState，并确认概览模块明确完成后，才记录 `PRODUCT_VERIFIED` 并进入下一步。

## 10. Package and publish for Microsoft Store

物料与包身份就绪后，执行商店正式发布流程：

1. **全新应用自动建项与身份回填**：
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -KeepOpen
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action reserve -AppName <AppName> -Manifest .\<app>\build\edge-store.json
   ```
2. **构建商店发布包（含 Assets 图标资源质检）**：
   ```powershell
   dotnet publish -c Release -r win-x64 -o ./publish
   # 确保 Assets 图标完整拷贝，杜绝 Partner Center 接受验证时报图像缺失错误
   if (Test-Path ./Assets) { Copy-Item -Recurse -Force ./Assets ./publish/ }
   New-Item -ItemType Directory -Force ./store-package | Out-Null
   winapp package ./publish --self-contained --executable <AppName>.exe --output ./store-package/<Identity>_<Version>_x64.msix
   ```
3. **清理权限声明与多设备依赖**：
   - 移除 manifest 中模板自带的未用特权（如 `systemAIModels`）；
   - **删除 `Windows.Universal` 依赖，确保仅声明 `Windows.Desktop`**（杜绝 Mobile/Xbox 设备全列 rank 1 报错）。
4. **多阶段离散步进与 DOM 深度自检（严禁一冲到底）**：
   - 先执行 Store 0 静态预检：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest <edge-store.json>`
   - 逐表单步执行并自检 DOM 状态：
     ```powershell
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase availability -Manifest <edge-store.json> -Apply
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase properties -Manifest <edge-store.json> -Apply
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase ageRatings -Manifest <edge-store.json> -Apply
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase packages -Manifest <edge-store.json> -Apply
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase listing -Manifest <edge-store.json> -Apply
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase options -Manifest <edge-store.json> -Apply
     ```
   - 概览页总检：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action verify -Manifest <edge-store.json>`
5. **最终提交**：
   - 概览页六项均完成后，只有显式 `-Action submit -ConfirmSubmit` 才点击「提交到应用商店」；
   - 记录 CLI 退出码、阶段 checkpoint、页面标题和最终 URL。

## 11. Technical run report

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

## 12. Record findings and update knowledge base

每轮结束后，Agent 在**工作根目录**里记录本轮技术经验（包括新发现的 Windows 坑点、新验证可行的命令与退出码），并可直接同步更新知识库：

```text
<工作根目录>/round-notes/round-N.md
```

内容包括：

- 新发现的 Windows 坑点（按 commands.md 的格式：Environment / Command / Error text / Root cause / Fix / Retest result）。
- 新验证可行的命令、参数组合和退出码。
- 设计/流程层面的教训与经验沉淀。

## References

- [discovery-interview.md](references/discovery-interview.md)：渐进式需求访谈建议。
- [partner-center-guide.md](references/partner-center-guide.md)：微软开发者中心（Partner Center）注册、包身份获取与商店提审全流程指引。
- [toolchain/README.md](references/toolchain/README.md)：工具版本与命令资料的职责。
- [toolchain/v1/commands.md](references/toolchain/v1/commands.md)：当前 Windows 命令和实测记录。
- [capabilities/registry.md](capabilities/registry.md)：可选能力建议与历史经验。
- [delivery-considerations.md](references/delivery-considerations.md)：联网、权限、发布和规模方面的设计提示。
- [test-assets.md](references/test-assets.md)：测试素材来源清单（图标/图片/音频/字体/数据，含许可红线）。
- [official-sources.md](references/official-sources.md)：官方文档入口。
- [project-readme-template.md](assets/project-readme-template.md)：每个 App 的 living README 起点。
