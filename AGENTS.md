# Quick App Maker V2 Agent Contract

## 目标

主线是 Node.js / Electron。所有新 App、CLI、测试和 Store 自动化都从 `bin/qam.mjs` 进入。旧的 C#、.NET、WinUI、PowerShell CDP 驱动已从 V2 删除。

## 工具链

- **禁止依赖与检查系统全局环境**：Agent 事先**不需要也不得**检查用户系统是否安装了 Git 或 Node.js，严禁调用用户系统全局环境；
- **全链路内置便携环境**：
  - Node.js 24 LTS portable：`WORKSPACE_ROOT/node/`；
  - Git portable（始终使用工作区版本）：`WORKSPACE_ROOT/git/`；
  - npm：随 Node 提供，使用工作区 npmrc 和 `.cache/npm`；
  - Electron：固定版本，二进制使用 China mirror 和工作区 cache；
  - Store：Playwright + `playwright-core` 连接独立 Edge；
  - MSIX：Electron production layout 后调用本地 `@microsoft/winappcli`。

Windows 命令统一通过 `bootstrap/qam.cmd` 进入；该包装器固定解析工作区 `node/node.exe` 与其内置 npm CLI。业务代码不直接启动 `npm.cmd`、`npx.cmd`，也不依赖 PATH 中的 Node/npm。

`qam-toolchain.lock.json` 从引擎目录加载：命令调用 `configureNpmEnvironment` 时必须传入引擎 lock，空工作区不需要手工复制一份到根目录。Edge 与 PowerShell 视为 Windows 内置能力；下载桥不探测系统 Git、Node 或 curl。

唯一保留的 PowerShell 是 bootstrap 下载桥和 Windows 默认桌面启动桥；业务状态、页面定位、编排、验证全部用 Node。

## 工作区边界

下载、缓存、日志、测试 profile、截图、DOM 和 checkpoint 必须写入当前工作区。禁止把工具工件写到系统临时目录、用户全局 npm 目录或工作区之外。启动系统 Edge 时只读取可执行文件位置，不读取用户日常 profile；生产 App 的系统 `userData` 由 App 自己使用，Agent 通过 UI 验证，不直接读取。

## Store 完成语义

控制器必须执行：

```text
PageKind → 完整 Observe → Diff → Apply
→ 当前 URL 冷导航 → 完整 Observe + Diff=0
→ Overview 模块显式 Complete → Converged
```

退出码：

| 码 | 含义 |
| ---: | --- |
| 0 | 阶段或总检已验证 |
| 1 | 执行错误或验证失败 |
| 2 | 配置、参数或 manifest 错误 |
| 3 | Node、Edge 或 Playwright 会话不可用 |
| 4 | 只读计划发现差异 |
| 5 | 页面 schema/selector 漂移 |
| 6 | 时间预算耗尽 |

`Converged` 证据必须同时包含 `pageKind`、`coldDiff`、`overviewStatus`、`overviewUrl`、证据文件 ID。Unknown、Processing、Error、重复包和缺少模块状态均保持未完成。

## 浏览器规则

1. 任何控件操作前先识别 `PageKind`。
2. 首选 `getByRole`、`getByLabel`、`getByText` 和 `getByTestId`。
3. Playwright Locator 默认穿透 open Shadow DOM；禁止保存会失效的节点索引。
4. 等待使用 Locator assertion、`waitForURL`、`waitForFunction` 或有截止时间的轮询；业务代码禁止固定 sleep。
5. 上传使用 `setInputFiles`；特殊 closed shadow 才进入集中 CDP fallback。
6. 会话使用工作区独立 profile，不接管用户日常 Edge。
7. CLI 不实现最终认证提交点击。

8. 开发模式默认关闭 DevTools；排查时显式设置 `QAM_DEVTOOLS=1`。

## 运行要求与进程模型

- **用户沟通零代码暴露（铁律）**：
  - **绝对禁止向用户提及任何代码、底层技术与内部术语**：严禁向用户询问或提及 `slug`、`IPC`、`脚手架`、`Electron`、`Vue`、`CSP`、`main.cjs`、`normalizeState`、`qam test` 等；
  - **英文目录名（slug）必须由 Agent 根据应用名称在内部自动推导**（如「回忆录」自动推导为 `memory-book`），严禁让用户理解或确认 slug；
  - **禁止向用户发送内部开发碎碎念**：代码编写过程必须在后台静默完成，严禁将思考过程、AST/DOM 解析、测试断言等作为可见消息发送给用户；
  - **交付必须直接在屏幕上打开应用**：验证完成后，通过后台异步启动 `qam dev` 直接将应用窗口拉起到用户屏幕前供其试用体验，**严禁将 PowerShell 命令甩给用户让用户去开终端敲命令**！
- **进程模型分界**：`qam dev` 为长驻 Watcher 进程（等待 Ctrl+C 退出），供用户在独立终端交互式运行。**Agent 在自动化执行流中严禁同步阻塞等待 `dev` 退出**（超时强杀会导致误判为程序崩溃）。
- **自动化验收依据**：Agent 的代码与契约质量验证统一使用 **`qam test .\app-slug`**（秒级退出并输出确定性退出码）。
- **业务编写强制性**：`qam create` 仅生成空骨架；Agent 必须接着修改 `index.html`、`app.js`、`styles.css` 落地真实业务，严禁将空骨架直接交付。
- **数据持久化契约**：必须且仅能使用 `window.qam.saveState()` 与 `window.qam.loadState()` 进行本地持久化，**严禁退化为 `localStorage`**；如需扩展数据模型，必须保证 `src/renderer/app.js` 与 `src/main/main.cjs` 的字段与类型定义严格对齐。
- **CSP 策略与 Vue 运行时**：纯源码模式下 `index.html` 的 CSP 必须包含 `script-src 'self' 'unsafe-eval'`，严禁移除 `'unsafe-eval'`（否则 Vue 3 无法在浏览器端动态编译模板，会导致 `[v-cloak]` 无法卸载引发彻底黑屏）。
- **禁止应试拼凑代码**：测试报错时必须排查真实的架构、数据流与逻辑根因，**严禁为了迎合静态断言而在源码中机械拼接死字符串**。
- **严禁直调底层**：严禁脱离 `bootstrap/qam.cmd` 直接调用底层 `electron.exe` 或使用 `--no-sandbox` 破坏沙箱隔离。
- **并发与锁控制**：同一 App 同时只允许一个 dev/package/store writer；进程清理只针对当前 run 的 PID/lock，严禁按名称批量强杀系统 Electron 或 Node。
- **结构化证据**：每次执行生成 run id 和结构化日志；checkpoint 原子写入并严格校验 manifest hash、productId 与 submissionId。
- **真实窗口交付**：源码正则或进程列表只算静态/启动证据；交付前必须完成真实窗口的输入、持久化、错误和关闭路径验证。

## 交付与发布协同

1. **首版试用交付**：业务开发与测试通过后，通过后台异步启动 `qam dev` 直接在用户屏幕上打开应用窗口体验，收集用户修改意见；
2. **微软商店发布协同（铁律）**：
   - 用户明确提出发布后，Agent 先执行 `qam store launch --app .\app-slug`（秒级拉起独立 Edge 浏览器）；
   - Agent **必须向用户发送登录指引消息并等待回复**：
     > “我已在屏幕上为您打开了 Edge 浏览器，请在弹出的窗口中登录您的微软账号（支持免费个人开发者账号）。**登录完成后，请在聊天框回复我说『我登录好了』，我接着为您全自动填表与上架！**”
   - Agent 结束当前发言，从容等待用户回复；
   - 用户在聊天框回复“我登录好了”后，Agent 接力执行全套自动化流水线：
     `store reserve` $\rightarrow$ `package --profile store` $\rightarrow$ `store preflight` $\rightarrow$ `store discover` $\rightarrow$ `store run --apply --confirm-age-ratings` $\rightarrow$ `store verify`；
   - 验证通过后，提示用户在浏览器中做最后的人工核对并点击「提交进行认证」。

