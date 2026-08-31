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
- **红线 3：严禁退化为 localStorage**：桌面应用必须通过 Electron 安全 IPC（`window.qam.saveState` 与 `window.qam.loadState`）进行本地持久化，**严禁私自替换为 `localStorage`**；主进程 `normalizeState` 负责数据校验。
- **红线 4：严禁破坏 CSP 的 unsafe-eval 声明**：纯源码模式下 Vue 3 需要在浏览器内编译 DOM 模板，`index.html` 的 CSP 必须保留 `script-src 'self' 'unsafe-eval'`，严禁移除（否则导致 Vue 挂载报错、`v-cloak` 无法清除引发彻底黑屏）。
- **红线 5：禁止脱离包装器直调底层**：严禁直接执行 `electron.exe` 或传 `--no-sandbox` 绕过框架。排查控制台时显式设置 `QAM_DEVTOOLS=1`。
- **红线 6：绝对禁止向用户暴露代码与技术黑话**：严禁向用户询问或提及 `slug`、`IPC`、`脚手架`、`Electron`、`Vue`、`CSP`、`normalizeState`、`qam test` 等内部代码与术语；英文 slug 由 Agent 自动推导；开发过程静默完成，严禁将代码思考碎碎念作为聊天消息发送；交付时直接在屏幕上拉起应用窗口，严禁把命令行甩给用户。

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

**只有用户明确提出发布时才进入**。

> [!NOTE]
> - **账号与认证免费**：微软个人开发者账号注册与认证是免费的。若用户未注册/未认证，执行 `store launch` 自动拉起 Edge 浏览器指引用户完成注册认证；
> - **云端自动签名**：MSIX 包由微软商店官方云端自动完成安全签名，**完全不需要开发者提供代码签名证书**，严禁向用户询问证书问题；
> - **严格遵守时序**：必须先 `store launch` 与 `store reserve`（获取 ProductId 与 Identity），再执行 `package`。

```powershell
# 1. 启动独立 Edge 引导用户登录/注册 Partner Center
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug

# 2. 保留应用名称并自动回填 ProductId 与 Identity 到 manifest
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 3. 生产封装生成 Store MSIX（必须在 reserve 之后执行）
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store

# 4. 离线静态预检（校验 manifest、素材尺寸与文案）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug

# 5. 发现或创建本次提交草稿
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

# 6. 一键自动化填写六大阶段（定价、属性、年龄分级问卷、程序包上传、商店文案与选项）
& "$qamRoot\bootstrap\qam.cmd" store run --app .\app-slug --apply --confirm-age-ratings --deadline 3600000

# 7. 冷加载总检验证（确认六个模块均为 Complete 绿标）
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
