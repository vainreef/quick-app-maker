# Quick App Maker V2 Agent Contract

## 目标

主线是 Node.js / Electron。所有新 App、CLI、测试和 Store 自动化都从 `bin/qam.mjs` 进入。旧的 C#、.NET、WinUI、PowerShell CDP 驱动已从 V2 删除。

## 工具链与便携沙箱

- **禁止依赖与检查系统全局环境**：Agent 事先不需要也不得检查用户系统是否安装了 Git 或 Node.js，严禁调用用户系统全局环境；
- **全链路内置便携环境**：
  - Node.js 24 LTS portable：`WORKSPACE_ROOT/node/`；
  - Git portable（始终使用工作区版本）：`WORKSPACE_ROOT/git/`；
  - npm：随 Node 提供，使用工作区 npmrc 和 `.cache/npm`；
  - Electron：固定版本，二进制使用 China mirror 和工作区 cache；
  - Store：Playwright + `playwright-core` 连接独立 Edge；
  - MSIX：Electron production layout 后调用本地 `@microsoft/winappcli`。

Windows 命令统一通过 `bootstrap/qam.cmd` 进入；该包装器固定解析工作区 `node/node.exe` 与其内置 npm CLI。业务代码不直接启动 `npm.cmd`、`npx.cmd`，也不依赖 PATH 中的 Node/npm。

## 工作区边界

下载、缓存、日志、测试 profile、截图、DOM 和 checkpoint 必须写入当前工作区。禁止把工具工件写到系统临时目录、用户全局 npm 目录或工作区之外。启动系统 Edge 时只读取可执行文件位置，不读取用户日常 profile；生产 App 的系统 `userData` 由 App 自己使用，Agent 通过 UI 验证，不直接读取。

## Store 完成语义

当前已跑通的单阶段执行语义：

```text
PageKind → Direct-Apply（完整填表）→ 保存收敛 → 记录检查点
```

六个阶段处理完成后，使用现有 `store verify` 进行总览验收。文档只描述已实际跑通的行为，不额外声称冷加载 Diff 或其他未执行机制。

退出码：
- `0`: 阶段或总检已验证
- `1`: 执行错误或验证失败
- `2`: 配置、参数或 manifest 错误
- `3`: Node、Edge 或 Playwright 会话不可用
- `4`: 只读计划发现差异
- `5`: 页面 schema/selector 漂移
- `6`: 时间预算耗尽

## 浏览器与表单铁律

1. **PageKind 准确识别**：任何控件操作前先识别 `PageKind`，具体表单（ListingForm/Grid/Form）优先级必须高于 Overview，严禁在未进入表单时误判为 Overview；
2. **选择器穿透与定位**：首选 `getByRole`、`getByLabel`、`getByText` 和 `getByTestId`；Playwright Locator 默认穿透 open Shadow DOM；
3. **Web Components 支持**：`<he-select>` 需通过内部 input 键入回车或 DOM 点击 `<he-option>` 触发；`<he-button>` 必须支持复合定位；
4. **可见性过滤（`:visible` 铁律）**：错误与警告断言必须严格过滤 `:visible` 伪类，严禁误读隐藏在 DOM 中的未激活错误模板节点；
5. **MSIX 打包 EBUSY 防御**：生产打包必须显式忽略 `.cache`、`build`、`out` 等运行时目录，防止复制被 Edge 独占锁定的 profile 引发 EBUSY；
6. **官方原生 `makemsix` 64KB 块对齐铁律**：微软云端严苛依赖 64KB 块边界物理对齐与 `AppxBlockMap.xml` 的 `LfhSize` 精确匹配；严禁使用第三方普通 ZIP 压缩生成 MSIX，必须通过微软官方跨平台编译的原生 `makemsix` 或 Windows 下 `@microsoft/winappcli` 打包；
7. **大包 CDP 注入**：50MB+ 大安装包上传必须使用 CDP 会话 `DOM.setFileInputFiles` 注入，规避 websocket 50MB 传输上限；
8. **受限功能显式等待**：「提交选项」页面若包含 `runFullTrust` 受限功能，必须显式 `waitFor` 异步审核文本域并填入本地离线隐私合规声明，消除 `<textarea class="has-error">` 校验拦截；
9. **静态官方说明横幅甄别**：保存与报错断言必须排除包含“错误”字样的静态官方声明横幅（如 `我们在你的 Package.appxmanifest`），严禁误判为保存失败；
10. **属性页隐私策略**：「属性」页面必须提供纯本地隐私策略声明文本（`#privacyPolicyText`），防止全信任桌面权限被微软后台拦截；
11. **完全就绪 DOM 调试**：发生报错时，必须显式等待所有 progressbar 和 loading 遮罩彻底卸载，抓取真实、完整的全量 DOM 结构与 ARIA 树进行深度分析；
12. **单阶段精准生效（最小操作集）**：严禁在已有模块完成时盲目跑全量 6 阶段轮询（`store run`），优先针对未完成模块执行 `store apply --phase <phase>`，最后通过 `store verify` 一次性总验；
13. **严格串行执行与 CDP 独占防死锁**：严禁并发派发多个 Edge / Playwright / Store 相关的命令或后台 Task；所有 CDP 操作必须单任务串行执行，前序任务彻底退出后方可执行下一个；
14. **全交互显式日志记录与禁绝控制台噪音**：所有与浏览器发生的交互（导航 goto、元素读取、输入 fill、选择 choose、保存 save、冷导航刷新 coldVerify、总览校验 overviewVerify）必须全部打印清晰明确的 `[BROWSER_ACTION]` 日志，严禁打印 `BROWSER_CONSOLE` 冗余控制台噪音；
15. **真机截图 `v-cloak` 彻底卸载与 Promise 契约铁律**：截屏渲染必须注入符合 `window.qam` 异步 Promise 契约（`appInfo` 必须返回 Promise，`loadState` 结构与 `app.js` 严格对齐）的真实 Mock，且截屏前必须显式断言 `[v-cloak]` 节点彻底卸载并确认核心文字节点已呈现，严禁截取未激活的空黑背景；
16. **程序包故障行与删除态自愈铁律**：程序包上传页面若出现 `已暂停 (Paused)`、`错误 (Error)` 或 `This package will be removed` 状态，必须自动识别并点击 `a[data-l10n-key="app_package_action_delete"]` 清理故障项，或点击 `a[data-l10n-key="app_package_action_revert"]` 恢复正常包状态，点亮保存按钮，严禁在故障项未清除时强行点击 disabled 保存按钮；
17. **云端大包解包异步等待与刷新重载铁律**：对于 50MB+ 的安装包，Partner Center 在云端解包验签需要 1~2 分钟。若提示“程序包需要长时间进行处理”，必须等待就绪或通过 `page.reload()` 重新同步云端真实就绪状态。

## 运行要求与进程模型

- **面向非技术用户的需求与交付沟通（铁律）**：
  - **抽象需求强阻断与停步等待（铁律）**：当用户仅给出单句意向、感性形容词或抽象愿望（如“想要记住重要的事情，打开有仪式感”、“想做个好看的记账软件”等缺少具体载体形式、核心操作闭环或功能边界的话语）时，**绝对严禁**自作主张判定为“信息充足”并直接启动项目创建或编码！
  - **方案具象化反问（A/B 双方案引导）**：Agent 必须基于用户的抽象愿景，具象化出 2~3 个具有明显差异的产品形态与视觉方案（例如方案 A：火漆印章羊皮纸卡片式，方案 B：倒计时时间胶囊式），明确说明各自的承载形式、核心操作与建议中文名供用户选择；
  - **强制结束发言并等待用户确认**：提问后 Agent **必须结束当前发言，从容等待用户回复**；严禁在同一轮次中擅自推进到 `qam create` 或代码编写，直到用户明确确认了具体形态方案或补充了明确的交互需求。
  - **绝对禁止向用户提及任何代码、底层技术与内部术语**：严禁向用户询问或提及 `slug`、`IPC`、`脚手架`、`Electron`、`Vue`、`CSP`、`main.cjs`、`normalizeState`、`qam test` 等；
  - **英文目录名（slug）必须由 Agent 根据应用名称在内部自动推导**（如「回忆录」自动推导为 `memory-book`），严禁让用户理解或确认 slug；
  - **禁止向用户发送内部开发碎碎念**：代码编写过程必须在后台静默完成，严禁将思考过程、AST/DOM 解析、测试断言等作为可见消息发送给用户；
  - **交付必须直接在屏幕上打开应用**：验证完成后，通过后台异步启动 `qam dev` 直接将应用窗口拉起到用户屏幕前供其试用体验，**严禁将 PowerShell 命令甩给用户让用户去开终端敲命令**！
- **维护者例外**：用户明确询问仓库、Skill、测试或故障原因时，可提供必要的文件、状态和验证记录。
- **进程模型分界**：`qam dev` 为长驻 Watcher 进程（等待 Ctrl+C 退出）。Agent 在后台异步启动并记录当前 run 的 PID；**自动化执行流中严禁同步阻塞等待 `dev` 退出**（超时强杀会导致误判为程序崩溃）。
- **自动化验收依据**：Agent 的代码与契约质量验证统一使用 **`qam test .\app-slug`**（秒级退出并输出确定性退出码）。
- **业务编写强制性**：`qam create` 仅生成空骨架；Agent 必须接着修改 `index.html`、`app.js`、`styles.css` 落地真实业务，严禁将空骨架直接交付。
- **数据持久化契约**：必须且仅能使用 `window.qam.saveState()` 与 `window.qam.loadState()` 进行本地持久化，**严禁退化为 `localStorage`**；如需扩展数据模型，必须保证 `src/renderer/app.js` 与 `src/main/main.cjs` 的字段与类型定义严格对齐。
- **CSP 策略与 Vue 运行时**：纯源码模式下 `index.html` 的 CSP 必须包含 `script-src 'self' 'unsafe-eval'`，严禁移除 `'unsafe-eval'`（否则 Vue 3 无法在浏览器端动态编译模板，会导致 `[v-cloak]` 无法卸载引发彻底黑屏）。
- **禁止应试拼凑代码**：测试报错时必须排查真实的架构、数据流与逻辑根因，**严禁为了迎合静态断言而在源码中机械拼接死字符串**。
- **严禁直调底层**：严禁脱离 `bootstrap/qam.cmd` 直接调用底层 `electron.exe` 或使用 `--no-sandbox` 破坏沙箱隔离。
- **并发与锁控制**：同一 App 同时只允许一个 dev/package/store writer；进程清理只针对当前 run 的 PID/lock，严禁按名称批量强杀系统 Electron 或 Node。

## 交付与发布协同（标准流水线）

1. **需求具象化与方案协同（铁律）**：面对单句或抽象需求，通过 A/B 方案引导用户确认产品形态与建议名称，并强制等待用户回复；用户明确确认后方可建项与开发；
2. **首版试用交付**：业务开发与测试通过后，通过后台异步启动 `qam dev` 直接在用户屏幕上打开应用窗口体验，收集用户修改意见；
3. **微软商店发布协同（五步标准流水线）**：
   - **第 1 步（登录与保留名称协同）**：
     - 用户明确提出发布后，Agent 先执行 `qam store launch --app .\app-slug`（秒级拉起独立 Edge 浏览器）；
     - Agent **必须向用户发送登录与应用命名指引消息并等待回复**：
       - 引导用户在弹出的 Edge 窗口中登录微软账号；
       - 提示用户登录后点击「新产品」$\rightarrow$「MSIX 或 PWA 应用」，**由用户亲自输入应用名称并点击「保留产品名称」**；
       - **向用户提供建议产品名称与备选名**；
       - **说明若名称已被占用的解决办法**（如增加特色修饰词、副标题、开发者标识等）；
       - 明确告知用户：**保留成功后，请在聊天框回复我说『我保留好了』或『下一步』！**
     - Agent 结束当前发言，从容等待用户回复；
   - **第 2 步（材料全面盘点与来源协同，铁律）**：
     - Agent **必须向用户全面列出 Microsoft Store 上架所需的全部材料清单与规格**：文案、MSIX 程序包图标、商店详情图片、可选推广图以及合规声明均以 Skill 的 `references/store.md` 为唯一口径；
     - Agent **必须主动询问用户材料来源意向**：
       - 方案 A（由 Agent 全自动生成与设计）：Agent 明确说明打算如何生成（真机渲染截取真实 1366x768 界面、主题设计 Logo、撰写文案）；
       - 方案 B（由用户亲自提供）：用户自行提供设计素材；
       - 方案 C（混合模式）：文案/截图由 Agent 生成，特定 Logo 由用户提供；
     - Agent 等待用户确认方案后，再开始执行真实素材生成与整理；
   - **第 3 步（真机素材生成、交互弹窗与用户确认，铁律）**：
     - 严禁使用脚手架纯色占位图；Agent 基于真实应用渲染捕获高清截图并设计图标；
     - Agent 将素材统一整理在 `store-submission-assets` 文件夹，**所有文件名必须全部采用纯中文清晰命名，明确标注用途与分辨率规格；文字说明绝对禁止使用 md，一律采用 txt 格式**（如 `00_时光回忆录_完整文案与亮点特性说明.txt`、`01_微软商店详情页_主运行界面高清截图_1366x768.png` 等）；
     - Agent **必须通过交互式任务指令拉起文件管理器，并通过 `OpenInputDesktop` 显式检测窗口句柄确保弹窗已置顶展示在用户屏幕前**；
     - Agent **向用户逐一说明每个素材的作用与规格，提醒用户核对确认**；
     - 图片的视觉效果以用户结论为准；用户说不合格即返回调整。模型缺少图片读取能力时，直接整理并打开素材文件夹，不代替用户做视觉判断；
     - 明确告知用户：**素材核对无误后，请在聊天框回复我说『确认素材』或『继续』，我接着为您全自动填报并上传！**
     - Agent 结束当前发言，从容等待用户确认；
   - **第 4 步（精准生效与全自动流水线接力，铁律）**：
     - 用户在聊天框回复“确认素材”或“继续”后，Agent 依据当前后台真实状态，**优先执行未完成模块的精准生效（`store apply --phase <phase>`），严禁对已有绿标的模块重复全量轮询**；
     - 填报完成后，执行 `store verify` 进行一键总体验收。
   - **第 5 步（人工最终审核与提交）**：
     - 验证通过（6 大模块全绿标）后，提示用户在浏览器中做最后的人工核对并点击「提交进行认证」。
