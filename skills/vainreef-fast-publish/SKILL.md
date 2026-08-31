---
name: vainreef-fast-publish
description: 用工作区内置的 Node.js、Git、Electron 和 Playwright 生成、试用、打包并准备 Microsoft Store 提交资料。
---

# Vainreef Fast Publish V2

## 0. 入场硬约束与防坑红线（Anti-Patterns）

- **便携沙箱绝对隔离**：Node.js 和 Git 只使用当前工作区的便携副本：`WORKSPACE_ROOT/node/`、`WORKSPACE_ROOT/git/cmd/git.exe`。严禁调用系统 Git，严禁执行全局 npm 安装。
- **统一命令入口**：首次入场只运行下载桥 `entry.ps1`；之后所有项目、测试和 Store 命令统一走 `bootstrap/qam.cmd`。
- **红线 1：长驻进程禁止同步阻塞等待**：`qam dev` 是热重载长驻 Watcher 进程（按 Ctrl+C 退出）。**AI Agent 在自动化流程中严禁前台同步阻塞等待 `dev` 退出**（否则触发超时强杀并引发崩溃误判）。Agent 自检必须使用秒级退出的 **`qam test .\app-slug`**，并提示用户在独立终端启动 `dev`。
- **红线 2：脚手架创建后必须编写业务代码**：`qam create` 仅生成通用骨架模板。Agent 必须紧接着修改 `src/renderer/index.html`、`app.js`、`styles.css` 实现具体业务功能，严禁跳过业务编写。
- **红线 3：禁止脱离包装器直调底层**：严禁直接执行 `electron.exe` 或传 `--no-sandbox` 绕过框架。排查控制台时显式设置 `QAM_DEVTOOLS=1`。

## 1. 阶段 1：需求访谈与脚手架生成（Phase 1）

先完成一次短访谈，并把答案写入生成项目的 `README.md`（参考 `references/discovery-interview.md`）：
1. 做什么、谁使用、何时打开；
2. 打开后的第一眼和一次完整操作；
3. 成功结果、错误提示和空状态；
4. 风格、素材、声音、网络与本地数据策略；
5. 第一版必须有、明确暂不做、暂定名称。

确认后直接生成脚手架并验证基础契约：

```powershell
$qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
& "$qamRoot\bootstrap\qam.cmd" doctor
& "$qamRoot\bootstrap\qam.cmd" bootstrap
& "$qamRoot\bootstrap\qam.cmd" self-test
& "$qamRoot\bootstrap\qam.cmd" create --name "应用名称" --slug app-slug
& "$qamRoot\bootstrap\qam.cmd" test .\app-slug
```

## 2. 阶段 2：App 核心业务实现（Phase 2: 编写代码）

**必须在生成的应用目录中修改代码实现真实业务**：

1. **页面结构**（`src/renderer/index.html`）：构建 UI 组件，所有插值 `{{ }}`、`v-*` 指令与 `@*` 事件必须位于 `#app` 挂载树内；
2. **状态逻辑**（`src/renderer/app.js`）：编写 Vue 3 原生响应式状态管理、核心功能算法与数据流。必须显式处理加载中、空数据、错误提示与数据持久化（`window.qam.loadState` / `saveState`）；
3. **界面样式**（`src/renderer/styles.css`）：编写深色现代排版与动效；
4. **主进程适配**（`src/main/main.cjs`）：若需特定持久化数据结构，同步调整 IPC 校验函数 `normalizeState`。

## 3. 阶段 3：自动化测试与用户试用体验（Phase 3）

1. **自动化测试验收**（Agent 必跑，秒级完成并输出证据）：
   ```powershell
   & "$qamRoot\bootstrap\qam.cmd" test .\app-slug
   ```
2. **用户真实窗口体验**（由用户在独立终端运行或通过守护进程启动）：
   ```powershell
   & "$qamRoot\bootstrap\qam.cmd" dev .\app-slug
   ```
3. **交付验收检查点**：
   - 空状态打开且控件可操作；
   - 正确输入能生效，空输入/非法输入给出明确错误提示；
   - 编辑、删除、操作结果可见且保存成功；
   - 关闭后重新打开，数据仍在；
   - 读取失败和保存失败不会把页面锁死；
   - 修改 renderer 后自动刷新，修改 main/preload 后自动重启。

## 4. 阶段 4：Microsoft Store 自动化发布（Phase 4）

**只有用户明确提出发布时才进入**：

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
最终认证提交由用户在已打开的浏览器页面中亲自复核并点击。

## 5. 证据与时间预算

- 每次命令生成 `.cache/qam/runs/<run-id>/`，至少有 `events.jsonl`、`run.log` 和 `result.json`；页面错误再保存 screenshot、ARIA snapshot、DOM 摘要、console/network 诊断。
- 证据不得包含凭据、cookie、token 或用户日常浏览器 profile；工作区外不写缓存和临时产物。
- 默认 60 分钟只作预算，不以计时器代替验证：bootstrap 10 分钟、MVP 20 分钟、动态验收 10 分钟、package/preflight 8 分钟、Store 12 分钟。超时保留 checkpoint 并明确返回码 6。
- 首版完成后邀请用户试用；收到反馈后小步修改并重新执行动态验收。用户确认满意且明确提出发布，再进入 Store。
