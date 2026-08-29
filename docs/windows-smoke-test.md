# Windows Smoke Test

这份手册用于真实 Windows 机器测试整条链路。每次测试都以实际输出为准；发现稳定坑点后，把结论同步到 `skills/vainreef-fast-publish/references/toolchain/v1/commands.md`。

## Test record

实测记录使用匿名轮次编号（轮次 1、轮次 2…）。具体 App 项目、会话日志与运行报告归档在 Agent 工作根目录（仓库父目录）下的各项目目录中，不入仓库、不 push。

第一轮实测记录（2026-08-22，轮次 1）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (Build · DeepSeek V4 Flash) |
| Repository commit | 60194b8 (注意：实机经验未推回，见 commands.md 会话级流程坑) |
| Windows edition | Windows 10 Enterprise 2009 |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 全新 Windows 实机，Bootstrap 从零安装 |
| Result | 链路全通（创建→Debug 运行→Release→publish→MSIX→安装→启动→卸载→重装），遇到 13 个坑已全部解决并写入 commands.md |

- 结论：模板 0.0.6-alpha + WinAppSDK 2.4.0 链路可用；ApplicationData 原生崩溃、调试身份冲突、PublishTrimmed、通知注册位置等坑见 commands.md「Confirmed Windows findings」
- 未执行：第 8 步重复 Bootstrap（本次未验证二次执行耗时）

第二轮实测记录（2026-08-22，轮次 2）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (DeepSeek V4 Flash → Ox Alpha) |
| Repository commit | 60194b8（仍未推回；第二轮基于旧基线开发，重踩了 0x80073CFB） |
| Windows edition | Windows 10 Enterprise 2009 |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 已有工具链，直接从工程阶段开始 |
| Result | 链路全通，最终交付 MSIX。新增 10 个坑点。但出现多次命令卡死（dotnet run 挂在后台导致），需用户手动打断；新增坑与卡死机制已全部写入 commands.md |

- 第二轮关键新发现：`dotnet run` 后台挂起机制（命令执行硬规则 1，7 次卡死：-PassThru 无效、重定向无效、开头杀 app 不够、中断残留污染下一条命令、判别律"命令返回⇔进程退出"）、StartupProbe ModuleInitializer 定位手段、`winapp ui` UI 自动化、`dotnet run` 清空 LocalState、Developer Mode 注册表、0x8007139F 孤儿 titlebar 调用
- 第二轮过程审计（2026-08-22 补充）：同一套设计被完整重推 4 次（占日志约 39%），决策反复 6+ 次，API 行为纯猜不实测（ScheduledToast 时限 6 轮、MicaBackdrop 11 轮、崩溃原因 12 回合）；通知三层状态机与双路径存储 fallback 属过度工程；设计纪律与 MVP-first 规则已写入 SKILL.md
- 未执行：第 8 步重复 Bootstrap

第三轮实测记录（2026-08-22，轮次 3）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (DeepSeek V4 Flash) |
| Repository commit | 04a78ce（本轮经验由主控合并回仓库） |
| Windows edition | Windows 10 Enterprise 2009（实际 26100/22631 镜像定制） |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 已有工具链，直接工程阶段 |
| Result | 链路全通：创建→Debug→Release→publish→MSIX（自包含）→安装→启动→UI 自动化全流程验证。新增 4 个坑点（0x80073CF3 框架依赖、0x80070490 自包含 auto-init、多 exe 歧义、CalendarDatePicker set-value），已并入 commands.md 第 22-25 条 |

- 第三轮关键新发现：自包含打包三件套（csproj `WindowsAppSDKSelfContained` + `winapp --self-contained` + `--executable`）；`winapp ui` 完成添加→列表→详情→删除全流程自动化验证；应用内 `IsSupported()` 检测在管理员会话正确降级并留日志
- 第三轮流程问题：Agent 按旧文档把项目写进了 `C:\Users\Administrator\Developer\apps-archive\`（违反工作目录规则）——已通过"工作目录规则"修正文档
- 未执行：第 8 步重复 Bootstrap；真实 toast 弹出（管理员会话系统限制，需普通会话验证）

第四轮实测记录（2026-08-22，轮次 4）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (DeepSeek V4 Flash) |
| Repository commit | 0a809b7（本轮经验由主控合并回仓库） |
| Windows edition | Windows 10 Enterprise 2009（实际 26100/22631 镜像定制） |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 已有工具链，直接工程阶段 |
| Result | 链路全通：创建→Debug→Release→publish→MSIX（自包含）→安装→启动→UI 自动化全流程验证（添加/编辑/删除/确认对话框）。新增 7 个坑点（26-32 条）与 4 条可复用组合，已并入 commands.md |

- 第四轮关键新发现：Window 无 Resources/Loaded、RectangleGeometry 无圆角、IsSupported 静态方法、manifest GUID 不带花括号、.ps1 中文需 BOM、图标按钮需 AutomationProperties.Name；数据预置法（写 LocalState JSON + images 目录）可全流程验证图片/提醒/清理，无需碰文件选择器
- 第四轮流程问题：工作目录规则已落实（项目/仓库/round-notes 同级），无 push；但 Agent 读取仓库文件产生了 CRLF 行尾符噪音（21 个文件假 modified），且直接读打包应用 LocalState 触发权限确认框——已新增"仓库行尾符保护"与"不直接读 LocalState"规则
- 未执行：第 8 步重复 Bootstrap；真实 toast 弹出（管理员会话系统限制，需普通会话验证）

第五轮实测记录（2026-08-23，轮次 5）：

| Field | Value |
| --- | --- |
| Date | 2026-08-23 |
| Tester | Agent (DeepSeek V4 Flash) |
| Target App | RememberWhat（记得什么） |
| Windows edition | Windows 10 Enterprise 2009（实际 26100/22631 镜像定制） |
| Windows build | 26100 |
| Architecture | x64 |
| Total Time | 约 31 分钟（全流程提速显著） |
| Result | 链路全通：访谈需求→本地生成渐变图库→自包含 MSIX 打包→安装运行→UI 自动化黑盒测试（添加/删除二次确认/分区/升级持久化）。新增 4 个坑点（33-36 条）已并入 commands.md |

- 第五轮关键新发现：WinAppSDK 2.4.0 缺失计划通知投影（改用 Windows.UI.Notifications.ToastNotificationManager + ScheduledToastNotification，坑 33）；ItemsControl 忘挂 ItemTemplate 直接 ToString 打印类型名（坑 34）；打包应用 LocalApplicationData 被系统重定向（坑 35）；winapp ui 列表操作多元素歧义需精准 UID 定位（坑 36）。
- 第五轮流程与人机交互教训：工程速度极快且零挂起，但在首版交付时出现了“向用户倾倒管理员通知限制技术黑话”与“过早推销商店上架”的沟通失准——已重构 SKILL.md「对用户的语言与心智规则」，确立“内部环境降噪屏障”与“交付首版是邀请把玩与共创迭代的起点，严禁在首版主动推销上架”的偏好引导。
- 未执行：第 8 步重复 Bootstrap；真实 toast 弹出（管理员会话系统限制，需普通会话验证）

第六轮实测记录（2026-08-23，轮次 6）：

| Field | Value |
| --- | --- |
| Date | 2026-08-23 |
| Tester | Agent (DeepSeek V4 Flash) |
| Target App | OldTimes（旧时光） |
| Windows edition | Windows 10 Enterprise 2009（实际 26100/22631 镜像定制） |
| Windows build | 26100 |
| Architecture | x64 |
| Result | 链路全通：定位并修复了导致 LLM 发狂调试的 XAML 假资源错误；自包含 MSIX 打包安装全通，UI 自动化测试添加/删除/编辑/二次确认全通过。新增 5 个坑点（37-41 条）并入 commands.md |

- 第六轮关键新发现与重大排障突破：
  1. DataTemplate 内部漏写 `x:DataType` 会导致 XamlCompiler 抛出假死错误 `WMC9999 ErrorMessages.resources`，极具误导性（坑 37）；
  2. WinUI 3 中 ContentDialog 漏设 `XamlRoot` 会直接触发 `0xc000027b` 原生闪退崩溃（坑 38）；
  3. `ScheduledToastNotification` 投影无 `Recurrence` 属性，每年提醒需循环单次调度（坑 39）；
  4. 证书导入严禁使用 `LocalMachine` 避免 UAC 提权弹窗，改用 `CurrentUser\TrustedPeople`（坑 40）；
  5. 国内 NuGet 还原镜像使用 `https://nuget.azure.cn/v3/index.json` 防超时（坑 41）。
- 第六轮重大教训：在 SKILL.md 注入“WinUI 3 一次性写对的 5 大黄金铁律”，彻底从源头杜绝 LLM 漏写 `x:DataType` 和 `XamlRoot` 造成的盲目试错与权限弹窗。
- 未执行：第 8 步重复 Bootstrap；真实 toast 弹出（管理员会话系统限制，需普通会话验证）

## 1. Run the public entry

在 Windows PowerShell 中执行：

```powershell
$entry = Join-Path (Get-Location).Path '.bootstrap-entry.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

记录：

- 最终是否出现 `BOOTSTRAP_READY`。
- 输出的 `Git executable` 实际路径。
- 总耗时。
- 下载和安装日志目录。
- 是否出现管理员确认。
- 发生错误时的原文和日志路径。

## 2. Inspect installed tools

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
Get-Command git.exe | Select-Object Source
git --version
dotnet --list-sdks
dotnet new list winui
Get-AppxPackage -Name winapp | Select-Object Name, Version, InstallLocation
winapp --version
winapp --help
```

记录输出：

```text
Git path:
Git version:
.NET SDK:
WinUI template:
WinAppCLI package version:
winapp --version:
```

## 3. Create a disposable WinUI project

```powershell
$smokeRoot = Join-Path (Get-Location).Path 'smoke-app'
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
Set-Location $smokeRoot

$appName = "VainreefSmokeApp$(Get-Date -Format 'yyyyMMddHHmmss')"
dotnet new winui-navview -n $appName
Set-Location $appName
```

记录：

- 创建命令退出码。
- 实际生成的 `.csproj` 路径。
- `Package.appxmanifest` 路径。
- 模板输出中的 warning / error。
- 工程目录结构。

## 4. Build and run

```powershell
dotnet build
$debugBuildExit = $LASTEXITCODE

dotnet run
$runExit = $LASTEXITCODE

dotnet build -c Release
$releaseBuildExit = $LASTEXITCODE
```

注意：`dotnet run` 会阻塞当前会话直到 App 退出（winapp 集成）。人工检查时窗口关闭后命令才会返回；自动化会话不要用它做后台启动（见 commands.md「命令执行硬规则 1」——7 次卡死教训，正确姿势是 `explorer shell:AppsFolder\<PFN>!App` 或结尾杀进程）。

人工检查：

- 窗口是否打开。
- 导航页是否显示。
- 窗口是否正常关闭。
- 第二次启动是否正常。
- Debug 和 Release 的退出码。

## 5. Publish

```powershell
dotnet publish -c Release -r win-x64 -o ./publish
$publishExit = $LASTEXITCODE
Get-ChildItem ./publish -Recurse | Select-Object FullName, Length
```

记录 Publish 输出目录、文件数量、总大小、warning 和退出码。

## 6. Inspect and run the packaging command

先保存当前工具帮助：

```powershell
winapp package --help | Tee-Object -FilePath ./winapp-pack-help.txt
```

然后试运行当前候选命令：

```powershell
winapp package ./publish --generate-cert --install-cert
$packExit = $LASTEXITCODE
```

根据真实输出记录：

- 最终采用的命令。
- MSIX 绝对路径。
- 证书路径。
- 管理员确认情况。
- warning / error。
- 退出码。

## 7. Install, launch, uninstall, reinstall

使用 `winapp package --help` 和打包输出给出的实际安装方式完成：

1. 首次安装。
2. 从开始菜单启动。
3. 关闭 App。
4. 卸载。
5. 使用同一包重新安装。
6. 再次启动。

记录每一步的命令、用户界面提示和结果。

## 8. Repeat Bootstrap

重新执行第 1 步，检查：

- 已安装 Git 是否被发现并复用。
- .NET、WinUI template 和 WinAppCLI 是否被跳过。
- Bootstrap 第二次执行耗时。
- 缓存和日志是否保持清楚。

## 9. Result summary

```text
BOOTSTRAP_READY:
Create project:
Debug build:
Run:
Release build:
Publish:
Pack:
Install:
Launch:
Uninstall:
Reinstall:
Second bootstrap:
```

## 10. Feed findings back

每个真实问题按下面格式写入 `commands.md`：

```markdown
### Short issue title

- Environment:
- Command:
- Exit code:
- Error text:
- Root cause:
- Fix:
- Retest result:
```

只写已经复现并重新测试过的结论。
