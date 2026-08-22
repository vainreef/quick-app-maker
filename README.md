# Vainreef Quick App Maker

> 新 Windows 电脑读取这一页后，一键准备 Windows App 工具链，然后立即进入 App 需求访谈。

## Agent 从这里开始

用户说：

```text
用 https://gitee.com/freevian/quick-app-maker README 开始。
```

Agent 直接执行下面三行。使用 `main` 分支，不使用 `master`：

```powershell
$entry = Join-Path $env:TEMP 'vainreef-quick-app-maker-entry.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

这三行就是唯一入口。Agent 不现场编写下载器、launcher、安装器或 clone 脚本。

一键脚本会安装 Git（缺时）、clone 仓库、安装 .NET SDK / WinAppCLI / WinUI 模板（只装缺的），完成后输出 `BOOTSTRAP_READY`。实现细节见 [bootstrap/README.md](bootstrap/README.md)。

## 一键安装完成时只输出

```text
BOOTSTRAP_READY
WORKSPACE_ROOT: <仓库的父目录>
NEXT_ACTION: read skills/vainreef-fast-publish/SKILL.md and start discovery
```

- `WORKSPACE_ROOT` 是 Agent 工作根目录，所有项目、临时文件、经验记录都放这里（与仓库同级）。
- 仓库目录只读：Agent 不改仓库内容、不 git add/commit/push。经验记录与坑点由外部主控合并回仓库。

## 开工前检查（每轮强制）

进入 Skill 前先确认仓库知识是最新的（只读操作）：

```powershell
git pull --ff-only origin main
# 确认 commands.md 头部 Status 行不是旧状态
Select-String -Path skills/vainreef-fast-publish/references/toolchain/v1/commands.md -Pattern '^\- Status'
```

如果上一轮 Agent 误改过仓库文件导致 pull 失败，丢弃本地残留（仓库里才是权威内容）：

```powershell
git -C <repo目录> checkout -- .
```

## 接下来立即进入 Skill

Agent 读取：

```text
skills/vainreef-fast-publish/SKILL.md
skills/vainreef-fast-publish/references/discovery-interview.md
```

然后向用户提问：

```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

用户给出第一段实质想法后，Skill 才创建真实 App 目录和 living README。用户确认完整需求后，才进入工程生成、运行、打包和 Store 流程。

**工作目录规则、命令执行硬规则、素材获取、设计纪律等全部见 SKILL.md——这是权威入口，README 不重复这些内容。**

## 项目结构

```text
.
├── README.md                       # Agent 入口（本页）
├── bootstrap/                      # 一键初始化实现（细节见 bootstrap/README.md）
├── skills/vainreef-fast-publish/  # Skill：需求发现、工程、打包和 Store 流程
├── toolchain/                      # 仓库内固定工具
├── docs/                           # Smoke Test 与研究资料
└── apps/                           # 用户作品本地目录（已在 .gitignore，绝不 push）
```

**仓库纯净性规则**：`apps/` 是用户放自己做好的应用的工作目录，只在本地使用，已在 `.gitignore` 中排除。任何具体 App 的源码、README、会话日志、运行报告、应用名都不得进入仓库或 push 到 Gitee——仓库只保存工具链与通用技术经验。