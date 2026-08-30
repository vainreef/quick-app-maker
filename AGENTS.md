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

## 运行要求

- 同一 App 同时只允许一个 dev/package/store writer；
- 每次命令生成 run id 和结构化证据；
- checkpoint 原子写入并校验 manifest hash、productId、submissionId；
- 页面错误保存 screenshot、ARIA snapshot、DOM 摘要、console/network 诊断；
- 进程清理只针对当前 run 的 PID/lock，不按名称批量结束 Electron 或 Node；
- 日常 App 开发零显式编译；MSIX 仅在发布阶段封装一次。
- 首次 bootstrap 先用便携 npm 建立 workspace 依赖，再加载 `bin/qam.mjs`；Electron 运行时下载完成并验证文件存在后才报告就绪。
- 源码正则或进程列表只算静态/启动证据；交付前必须完成真实窗口的输入、持久化、错误和关闭路径，并在 run-report 中保留证据。

## 交付

首版先邀请用户打开体验并收集修改意见；用户明确提出发布后，才进入 `reserve → package → preflight → store`。
