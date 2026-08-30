---
name: vainreef-fast-publish
description: 用工作区内置的 Node.js、Git、Electron 和 Playwright 生成、试用、打包并准备 Microsoft Store 提交资料。
---

# Vainreef Fast Publish V2

## 0. 入场硬约束

- Edge 和 PowerShell 按 Windows 内置能力处理；不把它们列为待安装依赖。
- Node.js 和 Git 只使用当前工作区的便携副本：`WORKSPACE_ROOT/node/`、`WORKSPACE_ROOT/git/cmd/git.exe`。Git 永远取工作区路径，不调用系统 Git，也不通过 `Get-Command git` 选取版本。
- 首次入场只运行下载桥 `entry.ps1`；之后所有项目、测试和 Store 命令统一走 `bootstrap/qam.cmd`。不执行全局 npm 安装，不手工复制 lock 文件来绕过错误。
- `qam-toolchain.lock.json` 属于 `quick-app-maker/` 引擎目录。命令会从引擎目录加载它，并把缓存固定到工作区；工作区根目录没有此文件也应能执行 `create`。
- `$qamRoot` 兼容“仓库直接在当前目录”和“仓库位于工作区/quick-app-maker”两种布局：

```powershell
$qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
& "$qamRoot\bootstrap\qam.cmd" doctor
```

- 不使用 `Get-Process electron | Stop-Process`、按名称批量杀进程或固定秒数等待。停止开发会话只操作该会话记录的 PID/终端；等待使用窗口、URL、日志或带截止时间的条件。
- 工作区已存在时先读取本地 `README.md`、`AGENTS.md`、skill 和 lock；不要把 Gitee HTML 整页抓回再重复阅读。先建立文件清单和验收矩阵，再按矩阵读取相关源码。

## 1. 需求与生成

先完成一次短访谈，并把答案写入生成项目的 `README.md`：

1. 做什么、谁使用、何时打开；
2. 打开后的第一眼和一次完整操作；
3. 成功结果、错误提示和空状态；
4. 风格、素材、声音、网络与本地数据策略；
5. 第一版必须有、明确暂不做、暂定名称。

确认后直接生成，不重复推演设计。生成后先按这个顺序验证基础闭环：

```powershell
& "$qamRoot\bootstrap\qam.cmd" bootstrap
& "$qamRoot\bootstrap\qam.cmd" doctor
& "$qamRoot\bootstrap\qam.cmd" self-test
& "$qamRoot\bootstrap\qam.cmd" create --name "应用名称" --slug app-slug
& "$qamRoot\bootstrap\qam.cmd" test .\app-slug
```

## 2. App 实现契约

- 先做“启动 → 读取 → 渲染 → 添加/修改/删除 → 保存 → 关闭重开”，再增加通知、文件或网络能力。
- Vue 的所有插值 `{{ }}`、`v-*` 指令和 `@*` 事件必须位于 `#app` 挂载树内；弹层也必须在树内，或使用经过测试的 Vue Teleport。脚本顺序必须是 Vue → 领域逻辑 → App。
- 每个异步读取、保存、通知和启动调用都要 `await` 或显式处理错误；保存失败时回滚内存状态并显示可操作的错误提示。
- 日期输入必须校验真实日历日期；倒计时按本地日历日计算，并在跨午夜后刷新。提醒范围和去重规则写入 README，测试中固定 `now`，不依赖机器当前时间。
- 必须提供加载中、空数据、输入错误、读取失败和保存失败状态。源码正则命中只算契约检查，不代表 UI 功能已通过。
- 开发模式默认不打开 DevTools；排查控制台时显式设置 `QAM_DEVTOOLS=1`，结束后恢复普通启动。
- `contextIsolation=true`、`sandbox=true`、`nodeIntegration=false`；IPC 使用白名单、发送方校验和参数校验。
- 每个小步同步项目 README 与 `build/run-report.md`，记录真实命令、退出码、耗时和证据路径。

## 3. 试用与验收闸门

日常源码运行：

```powershell
& "$qamRoot\bootstrap\qam.cmd" dev .\app-slug
```

交付“可试用”前必须完成真实窗口检查，而不是只看 Electron 进程存在：

1. 空状态打开且控件可操作；
2. 正确输入能添加，空名称/非法日期能给出提示；
3. 未来、过去、今天三种日期显示正确；
4. 编辑、删除和清除操作可见且保存成功；
5. 关闭后重新打开，数据仍在；
6. 当天庆祝层的文本已渲染，按钮、背景点击和 Esc 至少有一种关闭路径；
7. 读取失败和保存失败不会把页面锁死；
8. 修改 renderer 后刷新，修改 main/preload 后重启。

截图、控制台、网络诊断或自动化操作证据缺失时，在报告中标记“待动态验证”，不要勾选运行验收。

## 4. Store 流程

只有用户明确提出发布时才进入：

```powershell
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug
& "$qamRoot\bootstrap\qam.cmd" store run --app .\app-slug --apply --confirm-age-ratings --deadline 3600000
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug
```

每阶段都执行：

```text
PageKind → 完整 Observe → Diff → Apply
→ 当前 URL 冷导航 → 完整 Observe + Diff=0
→ Overview 模块 Complete → Converged
```

一轮 `store run` 使用同一个总截止时间；阶段切换不得重置预算。未知页面、Processing、Error、重复包、缺少模块或缺少证据都保持未完成。最终认证提交由用户在浏览器复核后点击。

Store 文案、截图和 Identity 在用户确认前保持待填状态；通用占位文案不算发布资料完成。

## 5. 证据与时间

- 每次命令生成 `.cache/qam/runs/<run-id>/`，至少有 `events.jsonl`、`run.log` 和 `result.json`；页面错误再保存 screenshot、ARIA snapshot、DOM 摘要、console/network 诊断。
- 证据不得包含凭据、cookie、token 或用户日常浏览器 profile；工作区外不写缓存和临时产物。
- 默认 60 分钟只作预算，不以计时器代替验证：bootstrap 10 分钟、MVP 20 分钟、动态验收 10 分钟、package/preflight 8 分钟、Store 12 分钟。超时保留 checkpoint 并明确返回码 6。
- 首版完成后邀请用户试用；收到反馈后小步修改并重新执行动态验收。用户确认满意且明确提出发布，再进入 Store。
