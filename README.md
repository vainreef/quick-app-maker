# Quick App Maker V2

用自然语言对话做出可试用的 Windows Electron 桌面应用，生成 MSIX 并自动化完成 Microsoft Store 提交资料。

---

## 核心设计哲学

- **零系统环境依赖**：执行前**严禁且无需检查**用户系统是否安装了 Git 或 Node.js，严禁要求用户手动安装任何全局环境；
- **全链路便携独立沙箱**：Node.js 24 LTS、MinGit、npm 缓存、Playwright、Electron 镜像全部自动下载并隔离在当前工作区根目录下；
- **业务与引擎解耦**：
  - `Project/`：用户的工作区根目录；
  - `Project/node/` & `Project/git/`：工作区内置便携运行环境；
  - `Project/quick-app-maker/`：核心工具链与自动化 CLI；
  - `Project/.agent/`：注入给 AI Agent 的规则、技能包与工作流 SOP；
  - `Project/<app-slug>/`：实际开发生成的具体业务应用。

---

## 第 0 步：一键入场（从全新空目录开始）

在一个全新的 Windows 工作区目录（如 `C:\Workspace\Project`）中，打开 PowerShell 运行以下指令：

```powershell
# 下载并执行一键引导脚本（下载便携 Node、便携 Git 并克隆 quick-app-maker）
$entry = Join-Path (Get-Location).Path '.qam-entry.ps1'
Invoke-WebRequest -UseBasicParsing `
  -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' `
  -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

> 首启脚本在仓库克隆前从 Gitee raw 下载。发布更新时，必须让 raw 版本与仓库中的 `bootstrap/entry.ps1` 保持同一版本。

### 执行后自动生成的目录结构

```text
Project/                                 <- 工作区根目录 ($workspaceRoot)
├── node/                                <- 【自动下载】内置 Node.js 24 LTS 便携环境
│   └── node.exe
├── git/                                 <- 【自动下载】内置 MinGit 便携环境
│   └── cmd/git.exe
├── .cache/                              <- 【工作区缓存】npmrc、electron 缓存，不污染系统全局
└── quick-app-maker/                     <- 【自动克隆】核心工具链仓库
    ├── bin/qam.mjs
    ├── packages/
    ├── skills/
    └── docs/
```

---

## 第 1 步：初始化 Agent 规则与技能（配置 `.agent` 目录）

引导完成后，在根目录执行以下指令，将 `quick-app-maker` 内的规则与技能复制到工作区根目录的 `.agent/`，让 AI Agent（Antigravity / Cursor / Claude 等）一打开项目即具备全套专家能力：

```powershell
# 1. 创建 .agent 目录结构
New-Item -ItemType Directory -Force -Path .agent\rules, .agent\skills\fast-publish, .agent\workflows | Out-Null

# 2. 复制规则与约束
Copy-Item -Force quick-app-maker\AGENTS.md .agent\rules\AGENTS.md
Copy-Item -Force quick-app-maker\docs\partner-center\运行契约.md .agent\rules\store-contract.md

# 3. 复制 fast-publish 核心技能包
Copy-Item -Recurse -Force quick-app-maker\skills\vainreef-fast-publish\* .agent\skills\fast-publish\

# 4. 复制标准工作流 SOP
Copy-Item -Force quick-app-maker\docs\v2\one-hour-runbook.md .agent\workflows\
Copy-Item -Force quick-app-maker\docs\v2\windows-smoke-test.md .agent\workflows\
```

### 配置完成后的完整工作区

```text
Project/
├── .agent/                              <- 【Agent 专属大脑】自动加载规则、技能与工作流
│   ├── rules/                           <- 运行约束（工具链沙箱、Store 契约）
│   ├── skills/fast-publish/             <- 核心技能（访谈、命令速查、Store 指南、README 模板）
│   └── workflows/                       <- 1 小时极速发布 runbook、冒烟验收清单
├── node/                                <- 便携 Node.js 运行时
├── git/                                 <- 便携 Git 运行时
├── .cache/                              <- 沙箱缓存
├── quick-app-maker/                     <- 自动化工具链引擎
└── my-app/                              <- 随后生成的具体业务应用
```

---

## 第 2 步：自然语言开发应用（零显式编译、纯源码直接运行）

> [!NOTE]
> 默认技术栈使用 **Node.js 24 LTS + Electron 44 + JavaScript + Vue 3 浏览器原生运行时**。日常开发直接运行源码，无需 Webpack/Vite 编译打包，保存即刷新。

### 开发指令清单

```powershell
# 1. 验证工具链与环境健康状态
.\quick-app-maker\bootstrap\qam.cmd doctor

# 2. 创建新应用（例如：倒计时时钟应用）
.\quick-app-maker\bootstrap\qam.cmd create --name "倒计时时钟" --slug countdown-app

# 3. 启动开发模式（监视 HTML/JS/CSS 自动刷新窗口，修改 main/preload 自动重启进程）
.\quick-app-maker\bootstrap\qam.cmd dev .\countdown-app

# 4. 运行单元与冒烟自动化测试
.\quick-app-maker\bootstrap\qam.cmd test .\countdown-app
```

开发完成后，先邀请用户直接在电脑上打开体验，收集反馈并持续迭代。

---

## 第 3 步：Microsoft Store 自动化发布（用户明确提出发布后触发）

当用户明确提出要上架发布时，触发完整的 Store 自动化流水线：

```powershell
# 1. 启动独立隔离的 Edge 浏览器，引导用户登录 Partner Center（不接管用户日常浏览器）
.\quick-app-maker\bootstrap\qam.cmd store launch --app .\countdown-app

# 2. 自动化保留应用名称，并自动回填应用 Identity 信息到 appxmanifest
.\quick-app-maker\bootstrap\qam.cmd store reserve --app .\countdown-app --name "倒计时时钟"

# 3. 生产封装生成符合 Store 规范的 MSIX 程序包
.\quick-app-maker\bootstrap\qam.cmd package .\countdown-app --profile store

# 4. 离线静态预检（严格校验 MSIX 格式、manifest 字段、图标资产尺寸与文案）
.\quick-app-maker\bootstrap\qam.cmd store preflight --app .\countdown-app

# 5. 发现或创建本次提交的草稿会话
.\quick-app-maker\bootstrap\qam.cmd store discover --app .\countdown-app

# 6. 一键自动化填写 Store 六大阶段（定价与可用性、属性、年龄分级、程序包、Store 一览与提交选项）
.\quick-app-maker\bootstrap\qam.cmd store run --app .\countdown-app --apply --confirm-age-ratings --deadline 3600000

# 7. 冷加载总体验证（确认六个模块冷加载后状态均为 Complete 绿标）
.\quick-app-maker\bootstrap\qam.cmd store verify --app .\countdown-app
```

> [!IMPORTANT]
> **人工提交安全边界**：
> `store verify` 全部通过后，CLI 不会自动点击最终的“提交进行认证”按钮。由用户在已打开的浏览器页面中审核各项资料，确认无误后亲自点击提交。

---

## 阶段断点与排错命令速查

在自动化过程中，如需对单个模块进行针对性审查或调试，可使用阶段断点命令：

```powershell
# 逐模块审查与填报
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase availability
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase properties
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase age-ratings --confirm-age-ratings
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase packages
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase listing
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase options

# 查看当前检查点与会话状态
.\quick-app-maker\bootstrap\qam.cmd store status --app .\my-app

# 停止当前 Edge 会话
.\quick-app-maker\bootstrap\qam.cmd store stop --app .\my-app
```

---

## 质量与网络保障

- **国内网络全量镜像**：Node 便携包与 npm 使用 npmmirror，Electron 二进制使用 China mirror，锁文件 `qam-toolchain.lock.json` 固化版本与 SHA-256；
- **原子下载与缓存保护**：所有下载先写 `.part` 临时文件，哈希校验一致后原子重命名；
- **全套自动化测试**：
  ```powershell
  .\quick-app-maker\bootstrap\qam.cmd check
  .\quick-app-maker\bootstrap\qam.cmd self-test
  ```
