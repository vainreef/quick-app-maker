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
- **红线 7：严禁使用纯色占位图片上架**：脚手架生成的 `solidPng` 纯色方块仅作为工程占位符，严禁直接作为上架素材提交到微软商店。屏幕截图必须来自应用真实运行界面的真机渲染，Logo 与磁贴图标必须根据应用主题深度设计。
- **红线 8：用户检视文件夹必须全中文清晰命名，文字禁止使用 md 一律使用 txt**：在拉起给用户检视的 `store-submission-assets` 文件夹内，**所有文件名称必须全部采用清晰的中文命名，且明确标注用途与分辨率规格**；**文字说明与文案文件禁止使用 `.md`，一律使用纯文本 `.txt` 格式**，方便用户双击直接用 Windows 系统自带记事本打开查看。
- **红线 9：禁止盲目全量轮询，遵循最小操作集与单阶段精准生效**：当已有模块处于完成状态（🟢）时，严禁盲目调用全量 `store run` 从头轮询重复刷新已完成页面。必须针对具体未完成模块执行 `store apply --phase <phase>` 精准生效，最后调用 `store verify` 一次性完成总览验收。
- **红线 10：失败排查必须基于完全就绪的真实 DOM Dump**：发生报错时，严禁在页面仍处于 progressbar 或 loading 阶段时草率判断；必须显式等待加载条彻底卸载，抓取真实、完整的全量 DOM 结构与 ARIA 树进行深度分析。

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

### 微软商店发布五步人机协同标准流程（铁律）

```text
第 1 步：登录与应用名称保留协同（用户在独立 Edge 亲自输入保留）
  ↓
第 2 步：上架材料全面盘点与来源协同（全面说明所需材料，询问是由 Agent 生成还是用户提供）
  ↓
第 3 步：真机素材生成、交互式弹窗与用户检视确认（全中文清晰文件名，纯文本 txt，explorer 弹窗置顶）
  ↓
第 4 步：按需精准生效与全自动流水线（按需执行 store apply --phase <phase>，最后 store verify 一次性总验）
  ↓
第 5 步：最终人工核对与提交（用户在浏览器中做最后检视并点击提交认证）
```

#### 详细执行步骤：

1. **第 1 步（登录与应用名称保留）**：
   - Agent 执行 `& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug` 拉起独立 Edge 浏览器；
   - Agent 发送登录指引、提供推荐名称、备选名称及重名解决方案；
   - **由用户亲自在 Edge 中输入应用名称并点击「保留产品名称」**；
   - 用户在聊天框回复「我保留好了」或「下一步」。

2. **第 2 步（上架材料全面盘点与来源协同）**：
   - Agent **必须向用户全面列出 Microsoft Store 上架所需的全部材料清单与规格**：
     - **文案类**：完整应用描述、一句话简短摘要、功能特性列表（3-5条）、搜索关键词（5-8个）；
     - **图像资产类**：`1366 x 768` 桌面高清主屏幕截图、`50x50 / 300x300` 商店徽标、`150x150` 开始菜单中磁贴、`44x44` 任务栏小图标、`310x150` 宽磁贴；
     - **合规声明类**：`runFullTrust` 纯本地离线使用理由、纯本地数据隐私策略文本。
   - Agent **必须主动询问用户材料来源意向**：
     - **方案 A（由 Agent 全自动生成与设计）**：Agent 明确说明打算如何生成（启动应用真机渲染截取 1366x768 高清界面、设计专属精致图标、撰写排版文案）；
     - **方案 B（由用户亲自提供）**：用户提供自己设计的图标与真机截图；
     - **方案 C（混合模式）**：文案与截图由 Agent 生成，特定 Logo 由用户提供。
   - 用户确认方案后，Agent 开始进行真实素材生成与整理。

3. **第 3 步（真机素材生成、交互式弹窗与用户检视确认，铁律）**：
   - Agent 拒绝使用脚手架的纯色占位图，必须基于真实应用运行界面捕获真实截图，并生成精致 Logo；
   - **全中文文件名与 txt 纯文本规范**：整理到 `store-submission-assets` 文件夹中的所有素材，**文件名必须全部采用中文，且明确写出用途和分辨率**（如 `01_微软商店详情页_主运行界面高清截图_1366x768.png` 等）；**文案与说明文件禁止使用 md，一律采用 `.txt` 格式**；
   - 使用交互式任务指令（`schtasks /create ... /f /it` 配合 `explorer.exe /select`）将文件夹弹窗置顶在用户屏幕正前方；
   - 利用 `OpenInputDesktop` + `EnumDesktopWindows` 显式校验 `CabinetWClass` 窗口句柄；
   - 在聊天框中向用户逐项说明每个素材的作用与规格，等待用户回复「确认素材」或「继续」。

4. **第 4 步（精准生效与全自动流水线接力，铁律）**：
   - 用户回复确认后，Agent 依据当前后台真实状态，**优先执行未完成模块的精准生效**，严禁对已完成模块重复跑全量轮询：
     ```powershell
     # 基础前置流程（初次进入）
     & "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"
     & "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store
     & "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug
     & "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

     # 单阶段精准填报（哪里未完成就填哪里，例如仅剩 listing 阶段）：
     & "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing

     # 最终一键总检验收（验证 6 大模块 100% 全绿标）：
     & "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug
     ```

5. **第 5 步（最终人工核对与提交）**：
   - 6 大模块全部打上绿标后，提示用户在 Edge 浏览器中做最后的人工核对并点击「提交进行认证」。

## 5. 证据与时间预算

- 每次命令生成 `.cache/qam/runs/<run-id>/`，至少有 `events.jsonl`、`run.log` 和 `result.json`；页面错误再保存 screenshot、ARIA snapshot、DOM 摘要、console/network 诊断。
- 整个 store 流程共享单一 deadline（默认 3600000ms），任何阶段超时都会干净退出。
- 默认 60 分钟只作预算，不以计时器代替验证：bootstrap 10 分钟、MVP 20 分钟、动态验收 10 分钟、package/preflight 8 分钟、Store 12 分钟。超时保留 checkpoint 并明确返回码 6。
- 首版完成后邀请用户试用；收到反馈后小步修改并重新执行动态验收。用户确认满意且明确提出发布，再进入 Store。

## 6. 避坑指南与已知错误防御（必背 16 项铁律）

### 1. 应用名称保留（人机协同边界）
- **铁律**：保留应用名称**必须由用户在独立 Edge 浏览器中亲自输入并确认**，严禁 Agent 越俎代庖自动输入或确认；
- **Agent 职责**：在拉起 Edge 后，主动向用户提供首选推荐名称、备选名称及重名解决方案（如增加特色副标题、修饰词或个人标识）；
- **接力时机**：明确提示用户在保留成功后回复「我保留好了」或「下一步」，收到回复后再接力执行全套自动化流水线。

### 2. 材料盘点与来源协同（生成前必问）
- **铁律**：在生成或收集素材前，**必须先向用户列清全部材料清单与规格，并询问材料来源（Agent 设计生成 vs 用户自行提供）**；
- **Agent 职责**：详细说明 Agent 的生成设计规划（如截图内容、Logo 主题与风格），尊重用户的定制诉求。

### 3. 拒绝纯色占位图与真机截图规范（IPC 模拟注入）
- **铁律**：脚手架 `png.mjs` 中的 `solidPng` 纯色方块仅为工程占位符，**严禁将纯色占位图作为上架素材交付或上传**；
- **错误场景与根因**：应用前端强依赖 Electron Preload 注入的 `window.qam.appInfo` 与 `loadState`。若在独立无头浏览器中渲染截图时未提前注入 `window.qam` mock，页面会触发 `Cannot read properties of undefined` 并进入错误告警态，导致截取的图片变成红色的错误报错界面；
- **防御准则**：在通过 Playwright 进行真机截图渲染前，必须通过 `page.addInitScript` 提前注入完整的 `window.qam` 模拟数据桥，注入丰富真实的数据卡片，并显式校验 `!locator('.error').isVisible()` 确保渲染无误后再截取保存。

### 4. 素材整理、交互式文件管理器弹窗、全中文命名与 txt 格式（人机协同边界）
- **铁律**：在向 Store 上传截图与录入文案前，**严禁 Agent 未经用户检视就直接静默上传**；
- **纯中文与纯文本 txt 规范**：`store-submission-assets` 文件夹内所有文件必须采用纯中文清晰命名；**文字说明绝对禁止使用 md，一律使用 txt 格式**；
- **终极防御准则（必背）**：
  1. 将全部文案（`.txt`）、真实截图（1366x768）、设计 Logo 整理到当前工作区目录下的 `store-submission-assets` 文件夹；
  2. 生成 `.txt` 纯文本文案说明文件；
  3. 使用交互式任务派发指令（`schtasks /create /tn "ShowAssets" /tr "explorer.exe /select,\"...\\00_时光回忆录_完整文案与亮点特性说明.txt\"" /sc once ... /f /it`），显式以活动桌面用户令牌启动并高亮文件；
  4. 利用 `OpenInputDesktop` 与 `EnumDesktopWindows` 显式检测 `CabinetWClass` 窗口已成功挂载在 `WinSta0\Default` 上，并获取到真实 HWND 句柄；
  5. 在聊天中向用户逐项说明每个素材的规格与作用；
  6. 明确提示用户确认无误后回复「确认素材」或「继续」，收到确认后方可执行最终填表。

### 5. 单阶段精准生效（最小操作集原则）
- **铁律**：严禁在已有模块完成时盲目跑全量 6 阶段轮询（`store run`）；
- **错误场景与根因**：全量轮询会对已打上绿标的页面强制做冷刷新、重复抓取和比对，徒增几十秒甚至数分钟的无效等待，还增加了网络偶发波动导致的失败概率；
- **防御准则**：排查或填报时优先使用 `store apply --app .\app-slug --phase <phase>` 直达目标页面单点突破，最后执行 `store verify` 一次性完成全局总验。

### 6. 打包文件锁定防御（EBUSY 防御）
- **错误场景**：执行 `package:store` 时因复制运行中的 Edge 用户目录导致 `EBUSY: resource busy or locked, copyfile ...\Default\Network\Cookies`；
- **防御准则**：`tools/package-store.cjs` 中的 `packager` 必须显式配置忽略规则：
  `ignore: [/^\/\.cache($|\/)/, /^\/build($|\/)/, /^\/out($|\/)/, /^\/store($|\/)/, /^\/tests($|\/)/, /^\/tools($|\/)/, /\.log$/]`，严禁把工作区运行时缓存或已打开的数据库文件打入包内。

### 7. Partner Center 现代化组件与 Shadow DOM
- **错误场景**：微软后台大量使用 Web Components 自定义标签（如 `<he-select>`、`<he-button>`、`<he-option>`、`<he-radio>`），原生 `selectOption` 或仅基于 `getByRole('button')` 会失效或超时；
- **防御准则**：
  - 对 `<he-select>`，通过其内部 input 输入文本并回车触发，或在展开后通过 DOM 点击 `<he-option>` 触发；
  - 对 `<he-button>`，选择器必须同时支持 `locator('he-button')`、原生 button 及文字过滤；
  - 弃用脆弱的选择器（如带斜杠的未转义属性选择器 `a[href*="/.../"]`），改用安全的 `a` 标签遍历与标准 API。

### 8. 隐藏 DOM 模板节点与可见性过滤（`:visible` 铁律）
- **错误场景**：Partner Center 页面包含大量预置在 DOM 中的不可见错误/警告模板节点，全量抓取导致未发生的错误被误报（如“我们在加载此页面时遇到问题”）；
- **防御准则**：在断言或检测页面错误时，必须严格使用 `:visible` 伪类过滤（如 `[role="alert"]:visible, .alert-error:visible`），严禁抓取隐藏模板文本。

### 9. 保存按钮幂等与禁用状态判定
- **错误场景**：当表单内容已保存或无修改时，页面保存按钮处于 `disabled` 状态，点击操作会发生 30 秒超时；
- **防御准则**：在执行 `save(page)` 前先检测按钮的 `disabled` 属性，若已禁用说明当前状态已收敛，直接安全放行。

### 10. SPA 异步渲染与正则表达式操作符优先级
- **错误场景**：单页应用路由切换时 URL 变化早于 DOM 渲染；动态构造正则表达式时由于 `|` 优先级最低导致捕获组失效；
- **防御准则**：
  - 页面跳转后必须通过 `waitUntil` 确保关键数据容器渲染完成再读取内容；
  - 动态拼接正则模式时，必须用非捕获括号封装：`new RegExp(`(?:${label})[^\\n\\t]*[\\t\\n]+\\s*([^\\n\\r]+)`, 'i')`。

### 11. 表单单选/复选遮挡与强制触发（`force: true` / DOM 派发）
- **错误场景**：在 IARC 年龄分级调查表等复杂 Angular 表单中，原生 `<input type="radio">` 上层覆盖有自定义 `<span class="response-titleText">`，导致 Playwright 标准点击判定为“被子树拦截指针事件”并触发 30 秒超时；
- **防御准则**：在点击单选/复选框时，必须使用 `{ force: true }` 或 DOM 级别的 `evaluate(el => el.click())` 派发 `change` 事件，确保表单选项即时生效。

### 12. 50MB+ MSIX 大安装包上传与 CDP 原生注入
- **错误场景**：MSIX 包体积通常达到 100MB~300MB。通过 `connectOverCDP` 控制的 Playwright 直接调用原生 `setInputFiles` 会触发 `Cannot transfer files larger than 50Mb to a browser not co-located with the server` 抛错崩溃；
- **防御准则**：必须通过 CDP 会话调用 `DOM.getDocument({ depth: -1, pierce: true })` 与 `DOM.setFileInputFiles({ files: [targetPath], nodeId })` 直接让宿主机浏览器加载本地大文件，并触发 `change` 事件。

### 13. 受限功能（runFullTrust）在「提交选项」页的必填审核理由
- **错误场景**：桌面 MSIX 应用声明了 `runFullTrust` 权限。在「提交选项（options）」页面，微软 Partner Center 会异步动态加载「受限的功能」专有审核区块（`<section>` 标题含 `受限的功能`），下方包含红色星号必填项：`为何需要使用 runFullTrust 功能，如何在产品中使用？*` 及 `<textarea class="text-area-width has-error">`。若未填写该理由，表单校验失败且「提交选项」在总览页上持续显示为「未完成」；
- **防御准则**：
  - 必须显式等待「受限的功能」区块渲染完成；
  - 必须自动填入合规的权限申请理由（如“本产品为基于 Windows 本地独立运行的桌面应用程序。需要使用 runFullTrust 权限以读写本地用户数据存储文件，实现记事和回忆笔记的本地安全持久化保存，不依赖也不连接任何外部云端网络服务。”）；
  - 触发 `input` 与 `change` 事件消除 `has-error`，确保「提交选项」表单成功保存并收敛为绿标。

### 14. 属性（properties）页全信任应用强制隐私策略
- **错误场景**：带有 `runFullTrust` 的应用在「属性」页保存时，微软后台强制要求提供隐私策略。若仅勾选“否，我的产品不使用任何个人信息”，系统仍会拦截并要求提供隐私策略 URL 或文本；
- **防御准则**：在「属性」页中选择「提供隐私策略文本（`#privacyPolicyText`）」，自动填入纯本地离线不收集个人数据的声明并保存，消除阻塞。

### 15. 失败排查必须基于完全加载就绪的真实完整 DOM（铁律）
- **错误场景**：页面发生错误或字段未找到时，由于网络延迟或 Angular SPA 仍在渲染，页面实际处于 `progressbar` 加载条未完成阶段。若此时草率抓取 DOM，只会获取到骨架或临时 loading 节点，导致误判选择器或做出错误结论；
- **防御准则**：
  - 遇到任何操作失败或表单未找到时，必须显式等待所有 `progressbar`、`.page-loading` 彻底卸载分离，并等待真实表单容器（如 `textarea`、`input`）渲染呈现；
  - 必须抓取真实、完整的全量 DOM 结构与 ARIA 树进行针对性字段分析与定位，严禁在未加载完成时草率下结论。

### 16. 表单输入基于真实 DOM 标签属性与事件派发（严禁凭经验臆想）
- **错误场景**：代码中脱离真实 DOM 凭经验假设选择器（如使用通用的 `name="description"` 或 `#description`），而微软 Partner Center 实际使用的真实 ID 是 `#description-required`，功能项是 `#feature-${i}`，搜索词是 `#search-terms he-select input`；
- **防御准则**：
  - 必须基于真实 DOM Dump 的精确标签 ID 与选择器进行输入定位；
  - 对 Angular 响应式表单，填充内容后必须主动派发 `input` 与 `change` 事件（对 keywords 输入框派发 `Enter` 键），触发 Angular 内部数据模型更新，确保点击保存时数据完整提交。

### 17. 严格串行执行与 CDP 独占锁防死锁（铁律）
- **错误场景与根因**：Edge 浏览器的远程调试端口（CDP）是独占式协议。当并发派发多个探查脚本或命令时，多个 Playwright CDP 实例同时争抢同一端口去控制同一页面，会导致 WebSocket 握手互斥与事件队列死锁，使所有并发 Task 全部卡死挂起；
- **防御准则**：
  - 绝对禁止并发派发任何 Edge / Playwright / Store 相关的命令或后台 Task；
  - 所有 CDP 连接与诊断脚本必须**严格单任务串行执行**；
  - 启动新命令前必须确认前一个进程/Task 已彻底断开 CDP 并正常退出；若前序任务异常，必须先显式清理杀死后再继续。

### 18. 浏览器全交互显式日志记录（铁律）
- **错误场景**：浏览器在后台执行页面跳转、DOM 操作或刷新校验时，由于缺少日志输出，用户和开发者无法感知脚本到底停留在哪一步、执行了什么操作，极易造成误判或盲目中断；
- **防御准则**：
  - 脚本与浏览器发生的任何交互（页面导航 goto、元素读取 readValue、文本输入 fill、下拉选择 choose/select、单选/复选 check、保存 save、总览校验 overviewVerify 等），**必须全部打印清晰明确的 `[BROWSER_ACTION]` 结构化日志**；
  - 严禁任何无日志输出的静默黑盒操作。

## 7. 「属性 (properties)」表单全字段与操作时序规范（SOP）

### 1. 表单全量字段与精确定位表
1. **主要类别（`name="CategorySelect"`，必填 \*）**：
   - **定位**：`select[name="CategorySelect"]`（所属容器 `#category-dropdown`）；
   - **值与中文映射**：`Productivity` $\rightarrow$ `生产率`，`BooksAndReference` $\rightarrow$ `书籍 + 参考`，`UtilitiesAndTools` $\rightarrow$ `实用工具 + 工具`，`Lifestyle` $\rightarrow$ `生活方式` 等；
2. **隐私策略声明（`name="privacyPolicySelection"`，必填 \*）**：
   - **对应问题**：`此产品是否访问、收集或传输个人信息(可用于识别个人身份的数据)?*`
   - **定位**：`select[name="privacyPolicySelection"]`；
   - **选项与动态行为**：
     - `value="No"`（`否，我的产品不使用任何个人信息`）：纯本地单机应用直接选此项，无额外输入框；
     - `value="Yes"`（`是，我的产品使用个人信息`）：动态展开单选框组：
       - ① `#privacyPolicyURL` 单选框：展开 `input[placeholder="Enter Privacy Policy URL"]`；
       - ② `#privacyPolicyText` 单选框：展开 `textarea[aria-label="提供隐私策略文本"]`（用于填入纯文本离线隐私策略声明）；
3. **产品声明（复选框组）**：
   - `storage-checkbox`：默认勾选（支持安装到备用存储）；
   - `backups-checkbox`：默认勾选（支持 OneDrive 自动备份）；
   - `windows-checkbox`：默认勾选（录制与广播支持）；
   - `store-checkbox`、`accessibility-checkbox`、`penInk-checkbox`、`usesGenAI-checkbox`：默认未勾选；
4. **支持信息（可选）**：
   - 网站 / 支持邮箱 / 电话 / 地址行，默认可留空；
5. **保存提交按钮**：
   - **定位**：`button[name="save_button"]`（`<button type="submit" name="save_button" class="btn btn-primary"><span>保存</span></button>`）。

### 2. 标准操作时序（先点什么后点什么，精准无误）
1. **第 1 步（选择主要类别）**：定位 `select[name="CategorySelect"]`，选择 `Productivity`，并派发 `change` 和 `input` 事件触发 Angular 脏检查；
2. **第 2 步（选择隐私声明）**：定位 `select[name="privacyPolicySelection"]`，选择 `No`（或按需选 `Yes` $\rightarrow$ 点击 `#privacyPolicyText` 注入文本），并派发 `change` 和 `input` 事件；
3. **第 3 步（等待渲染就绪）**：等待 500ms 确保页面无红字校验报错；
4. **第 4 步（点击保存按钮）**：定位并点击 `button[name="save_button"]`，等待保存完成（无报错提示），直接完成该阶段！

## 8. 「定价和可用性 (availability)」表单全字段与操作时序规范（SOP）

### 1. 表单全量字段与定位表
1. **市场选择（`marketSelection`）**：
   - `input[type="radio"][name="marketSelection"][value="true"]` $\rightarrow$ `全球所有市场`（默认选中）；
   - `input[type="radio"][name="marketSelection"][value="false"]` $\rightarrow$ `限定特定市场`；
2. **定价层级（`priceTier`，`<he-select>` 快速键入铁律）**：
   - 定位：`price-tier-selection[pricetierkey="Retail"] he-select input`；
   - **交互铁律**：`<he-select>` 内部为标准 `role="combobox"` 的文本输入框（`<input class="text-field__control">`），**无需繁琐遍历数百个选项，直接定位内部 `input` 填入目标值（如 `'0'` 或 `'CNY'`）并回车（`press('Enter')`）即可秒级生效**；
   - 默认值：`0`（免费 Free）；
3. **免费试用类型（`TrialType`）**：
   - 定位：`select[aria-label="TrialType"]`；
   - 默认值：`string:NoTrial`（无免费试用）；
4. **受众与可见性（`Audience & Visibility`）**：
   - 受众单选：`#radioDistribution_PublicAudience` $\rightarrow$ `开放受众`（默认选中）；
   - 可见性单选：`#radioVisibility_Public` $\rightarrow$ `使此产品可用并在 Microsoft Store 中可发现`（默认选中）；
5. **组织批量分发许可（`Enterprise Licensing`）**：
   - `#enterpriseonline_checkbox` $\rightarrow$ 托管(联机)许可（默认勾选）；
6. **保存按钮**：
   - 定位：`#saveButtonPricing` 或 `input[type="submit"][value*="保存"]`。

### 2. 标准操作时序（先点什么后点什么）
1. **第 1 步（市场确认）**：确认 `input[name="marketSelection"][value="true"]` 为选中状态；
2. **第 2 步（定价确认）**：点击 `price-tier-selection he-select` 展开下拉选项，选择基础价格层级为 `0`（免费）；
3. **第 3 步（受众与可见性确认）**：确保受众为 `Public`，可见性为 `Public`；
4. **第 4 步（点击保存）**：定位并点击 `#saveButtonPricing`，等待保存完成！
