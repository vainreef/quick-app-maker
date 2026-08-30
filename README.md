# Quick App Maker V2

用自然语言做出可试用的 Windows Electron 应用，再生成 MSIX 并完成 Microsoft Store 提交资料。

## 一条主线

```text
bootstrap → 需求访谈 → create → dev → test → 用户试用
→ reserve → package → preflight → Store 六阶段 → verify → 用户提交
```

默认 App 使用 **Node.js 24 LTS + Electron 44 + JavaScript + Vue Runtime**。开发过程直接运行源码，不执行实时编译；只有发布 MSIX 时做一次 production layout 和封装。

## 一条命令入场

> [!IMPORTANT]
> **统一使用内置独立环境，禁止检查与使用系统全局环境**：
> 1. 执行前**不需要检查**用户机器是否预装了 Node.js 或 Git，严禁让用户手动去系统安装任何依赖；
> 2. **严禁调用用户系统全局的 Node.js/Git/npm**，所有操作必须统一使用 bootstrap 部署在当前工作区内的独立便携环境（`WORKSPACE_ROOT/node/`、`WORKSPACE_ROOT/git/`）；
> 3. Agent 与开发者直接按照文档流程执行入场脚本，环境准备与沙箱配置全部由脚本自动完成。

在一个全新的 Windows 工作目录执行：

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force
$entry = Join-Path (Get-Location).Path '.qam-entry.ps1'
Invoke-WebRequest -UseBasicParsing `
  -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' `
  -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

入口只负责下载工作区 portable Node，之后所有流程由 `node bin/qam.mjs` 执行。

## 快速命令

```powershell
node .\bin\qam.mjs doctor
node .\bin\qam.mjs create --name "我的应用" --slug my-app
node .\bin\qam.mjs dev .\my-app
node .\bin\qam.mjs test .\my-app
```

创建完成后，使用 `my-app/README.md` 记录需求和验收标准。`dev` 监视 JS/HTML/CSS，renderer 变更自动刷新，main/preload 变更自动重启。

## 发布命令

```powershell
node .\bin\qam.mjs store launch --app .\my-app
node .\bin\qam.mjs store reserve --app .\my-app --name "我的应用"
node .\bin\qam.mjs package .\my-app --profile store
node .\bin\qam.mjs store preflight --app .\my-app
node .\bin\qam.mjs store discover --app .\my-app
node .\bin\qam.mjs store run --app .\my-app --apply --confirm-age-ratings --deadline 3600000
```

需要逐阶段审查时，用下面的断点命令替代 `store run`：

```powershell
node .\bin\qam.mjs store apply --app .\my-app --phase availability
node .\bin\qam.mjs store apply --app .\my-app --phase properties
node .\bin\qam.mjs store apply --app .\my-app --phase age-ratings --confirm-age-ratings
node .\bin\qam.mjs store apply --app .\my-app --phase packages
node .\bin\qam.mjs store apply --app .\my-app --phase listing
node .\bin\qam.mjs store apply --app .\my-app --phase options
node .\bin\qam.mjs store verify --app .\my-app
```

`verify` 只在六个模块冷加载后均为 `Complete` 时返回 0。最终“提交进行认证”由用户在浏览器中审核后点击，CLI 不提供自动提交命令。

## 目录

```text
bin/qam.mjs                         CLI 入口
packages/core/                      路径、日志、下载、进程和配置
packages/generator-electron/        Electron 模板生成
packages/store-core/                Desired/Diff/Checkpoint/证据状态机
packages/store-playwright/          Edge 会话、PageKind、六个阶段
packages/store-preflight/           MSIX、manifest、素材静态预检
templates/electron-vue-runtime/     默认无编译 Electron App 模板
skills/vainreef-fast-publish/       V2 Skill 和短命令手册
docs/v2/                             架构、迁移、Windows smoke test
```

## 国内网络

版本、URL 和 SHA-256 只在 `qam-toolchain.lock.json` 中维护：

- Node portable：npmmirror；
- npm registry：npmmirror；
- Electron binary：Electron China mirror；
- npm cache、Electron cache、下载日志：当前工作区 `.cache/`；
- Playwright 使用 `playwright-core`，不下载额外浏览器。

所有下载先写 `.part`，成功校验后原子改名；同一损坏工件最多重试一次。

## 开发效率目标

| 项目 | V2 门槛 |
| --- | --- |
| 日常显式编译 | 0 次 |
| renderer 修改生效 | ≤ 1.5 秒 |
| main/preload 修改生效 | ≤ 3 秒 |
| 第二次 bootstrap | 零下载 |
| Store 完成 | cold diff=0 + Overview Complete |
| 默认包上传 | 同名唯一 `Validated` |
| 工具状态 | 全部在工作区 `.cache/` |

## 质量命令

```powershell
node .\bin\qam.mjs check
node --test
git diff --check
```

真实 Windows 验收见 `docs/v2/windows-smoke-test.md`。Partner Center 执行边界见 `docs/partner-center/运行契约.md`。

## 官方资料

- [Node.js Releases](https://nodejs.org/en/about/previous-releases)
- [Electron 安装与镜像](https://www.electronjs.org/docs/latest/tutorial/installation)
- [Microsoft Electron + WinApp CLI](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/electron-setup)
- [Microsoft Electron MSIX](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/electron-packaging)
- [Microsoft Store Win32 分发](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)
- [Playwright Locators](https://playwright.dev/docs/locators)
