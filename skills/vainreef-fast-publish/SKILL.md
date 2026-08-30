---
name: vainreef-fast-publish
description: 用 Node.js、Electron 和 Playwright 在 Windows 工作区内快速生成、试用、打包并准备 Microsoft Store 提交的完整流程。
---

# Vainreef Fast Publish V2

## 核心规则

- **绝对使用工作区环境，禁止检查系统环境**：事先**无需检查**用户是否安装 Node.js/Git，**严禁使用**用户系统全局环境，所有操作统一使用 bootstrap 在工作区内准备的便携 Node/Git 沙箱，直接按流程执行；
- 默认技术栈：Node.js 24 LTS、Electron 44、JavaScript、Vue Runtime；
- 默认模板 `electron-vue-runtime` 不使用 bundler，不使用 TypeScript emit；
- 所有命令从 `node bin/qam.mjs` 进入；
- 下载、cache、日志、测试 profile 和证据都位于当前工作区；
- 首版先交付试用，用户明确提出发布后才进入 Store；
- 商店自动化到 `verify` 结束，最终认证提交由用户审核并点击。

## 开始

用户只需要描述想做的 App。按照访谈顺序记录到生成项目的 `README.md`：

1. 做什么；
2. 谁会用、何时打开；
3. 打开后的第一眼和核心闭环；
4. 风格、素材、数据和网络需求；
5. 第一版验收条件；
6. 暂定名称和不做范围。

用户确认后立即生成，不重复推演整套设计。

## 开发流程

```powershell
node .\bin\qam.mjs bootstrap
node .\bin\qam.mjs create --name "应用名称" --slug app-slug
node .\bin\qam.mjs dev .\app-slug
node .\bin\qam.mjs test .\app-slug
```

`dev` 直接运行源码。renderer 的 JS/HTML/CSS 变化刷新窗口；main/preload 变化重启进程。日常修改不执行 build、不生成 MSIX。

实现规则：

- 先实现启动 → 数据存取 → 渲染 → 关闭重开；
- 再逐项加入通知、文件、网络或系统能力；
- IPC 采用白名单、sender 校验和参数校验；
- `contextIsolation=true`、`sandbox=true`、`nodeIntegration=false`；
- 默认依赖保持纯 JS，原生 addon 单独建立 capability profile；
- 每一小步更新项目 README 和 `build/run-report.md`。

## Store 发布

用户表达“发布到商店”后按顺序执行：

```powershell
node .\bin\qam.mjs store launch --app .\app-slug
node .\bin\qam.mjs store reserve --app .\app-slug --name "应用名称"
node .\bin\qam.mjs package .\app-slug --profile store
node .\bin\qam.mjs store preflight --app .\app-slug
node .\bin\qam.mjs store discover --app .\app-slug
node .\bin\qam.mjs store run --app .\app-slug --apply --confirm-age-ratings --deadline 3600000
```

需要逐阶段审查时，用下面的命令替代 `store run`：

```powershell
node .\bin\qam.mjs store apply --app .\app-slug --phase availability
node .\bin\qam.mjs store apply --app .\app-slug --phase properties
node .\bin\qam.mjs store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
node .\bin\qam.mjs store apply --app .\app-slug --phase packages
node .\bin\qam.mjs store apply --app .\app-slug --phase listing
node .\bin\qam.mjs store apply --app .\app-slug --phase options
node .\bin\qam.mjs store verify --app .\app-slug
```

`--confirm-age-ratings` 表示用户已检查问卷；缺失时年龄分级阶段停在配置检查。

### 每阶段裁决

必须满足：

```text
PageKind → Observe → Diff → Apply
→ 当前 URL 冷导航 → Observe → Diff=0
→ Overview 对应模块 Complete → checkpoint=Converged
```

退出码：`0` 已验证，`1` 执行错误，`2` 配置错误，`3` 会话错误，`4` 只读发现差异，`5` 页面 schema 漂移，`6` 超出时间预算。

### 上传规则

- 0 个同名包：上传一次；
- 1 个 Processing：等待；
- 1 个 Validated：跳过上传；
- Error 或重复：停止并执行 repair；
- 文件名出现不代表成功；唯一 `Validated` 才算成功。

### 证据

每次命令生成 `.cache/qam/runs/<run-id>/`，包含 `events.jsonl`、`result.json`、阶段 JSON、截图、ARIA 和 DOM 摘要。凭据、cookie、token 和用户浏览器 profile 不写入证据。

## 时间预算

默认总预算 60 分钟：

- bootstrap 10 分钟；
- 生成和 MVP 20 分钟；
- smoke/E2E 10 分钟；
- package/preflight 8 分钟；
- Store 六阶段 12 分钟。

任何等待都带截止时间和进度日志。超出预算返回 6，并保留 checkpoint，下一次从当前阶段继续。

## 国内网络

只使用 `qam-toolchain.lock.json` 的版本和镜像。npm、Electron、Node 下载使用工作区 cache；Playwright 使用 `playwright-core`，不下载额外浏览器。镜像不可用时读取已有 cache 和诊断日志，不临时更换依赖版本。

## 交付话术

首版完成后只告诉用户应用已经生成并邀请试用。收到反馈后小步修改并重新 `dev/test`。用户确认满意并提出发布，再进入商店链路。
