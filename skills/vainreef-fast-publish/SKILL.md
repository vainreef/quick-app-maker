---
name: vainreef-fast-publish
description: 微软商店全自动发布专家技能。基于可配置浏览器基础设施 (默认 Chrome，支持 Edge 与 Safari) + Playwright 自动化驱动，贯穿 Electron 应用创建、质量验收、MSIX 封装及 Partner Center 六大阶段直接填报与总检。
---

# Vainreef Fast Publish V2 (微软商店全自动发布核心技能)

## 0. 核心架构：浏览器外围基础设施解耦与运行机制

### 1. 为什么将浏览器作为外围可插拔基础设施？
- **业务与浏览器解耦**：底层网页（Partner Center / DOM 树 / 表单填报 Direct-Apply）在各大现代浏览器中是完全一致的，浏览器仅作为**最外层的宿主环境与基础设施**；
- **多浏览器自由切换（全局可配置）**：
  - **`chrome` (Google Chrome)**：**当前开发与测试默认浏览器**；
  - **`edge` (Microsoft Edge)**：Windows 生产发布与 Partner Center 官方推荐；
  - **`safari` (Safari / WebKit)**：macOS 原生轻量环境；
  - 支持通过 CLI 参数 `--browser chrome|edge|safari`、环境变量 `QAM_BROWSER` 或配置文件动态指定；
- **全链路便携隔离沙箱**：
  - 启动时自动分配独立沙箱目录：`--user-data-dir=.cache/qam/session/profile-<browserType>-<runId>`；
  - **绝不读取、绝不依赖、绝不污染**用户日常浏览器的个人配置、Cookies 与书签数据；
  - 统一设置 `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`，跳过繁重的无头二进制下载；
- **CDP (Chrome DevTools Protocol) 直连控制**：
  - 启动浏览器实例时暴露独立调试端口 `--remote-debugging-port=<freePort>`；
  - Node.js 端通过 `playwright-core` 的 `chromium.connectOverCDP()` 直连调试端口，驱动页面与 Tab 管理。
---

## 1. 入场硬约束与防坑红线（Anti-Patterns）

- **便携沙箱绝对隔离**：Node.js 和 Git 只使用当前工作区的便携副本：`WORKSPACE_ROOT/node/`、`WORKSPACE_ROOT/git/cmd/git.exe`。严禁调用系统 Git，严禁执行全局 npm 安装。
- **统一命令入口**：首次入场只运行下载桥 `entry.ps1`；之后所有项目、测试和 Store 命令统一走 `bootstrap/qam.cmd`（macOS/Linux 下走 `node bin/qam.mjs`）。
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

---

## 2. 应用开发完整流水线（从访谈到交付）

### 阶段 1：需求访谈与脚手架生成
先完成一次简短访谈（参考 `references/discovery-interview.md`）：
1. 做什么、谁使用、何时打开；
2. 打开后的第一眼和一次完整操作；
3. 成功结果、错误提示和空状态；
4. 风格、素材、声音、网络与本地数据策略；
5. 第一版必须有、明确暂不做、暂定中文名称（英文 slug 内部自动推导）。

确认后直接生成脚手架并验证基础契约：
```powershell
# Windows 环境
$qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
& "$qamRoot\bootstrap\qam.cmd" doctor
& "$qamRoot\bootstrap\qam.cmd" bootstrap
& "$qamRoot\bootstrap\qam.cmd" self-test
& "$qamRoot\bootstrap\qam.cmd" create --name "应用名称" --slug app-slug
& "$qamRoot\bootstrap\qam.cmd" test .\app-slug
```

### 阶段 2：App 核心业务实现
必须在生成的应用目录中修改代码实现真实业务：
1. **页面结构**（`src/renderer/index.html`）：构建 UI 组件，所有插值 `{{ }}`、`v-*` 指令与 `@*` 事件必须位于 `#app` 挂载树内；
2. **状态逻辑**（`src/renderer/app.js`）：编写 Vue 3 原生响应式状态管理、核心功能算法与数据流。必须显式处理加载中、空数据、错误提示与数据持久化（`window.qam.loadState` / `saveState`）；
3. **界面样式**（`src/renderer/styles.css`）：编写深色现代排版与动效；
4. **主进程适配**（`src/main/main.cjs`）：若需特定持久化数据结构，同步调整 IPC 校验函数 `normalizeState`。

### 阶段 3：自动化测试与用户试用体验
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

---

## 3. Microsoft Store 自动化发布（五步人机协同标准流程）

**只有在用户明确提出上架发布时才进入此阶段。**

```text
第 1 步：登录与应用名称保留协同（用户在独立 Edge 亲自输入保留）
  ↓
第 2 步：上架材料全面盘点与来源协同（全面说明所需材料，询问是由 Agent 生成还是用户提供）
  ↓
第 3 步：真机素材生成、交互式弹窗与用户检视确认（全中文清晰文件名，纯文本 txt，置顶弹窗检视）
  ↓
第 4 步：按需精准生效与全自动流水线（按需执行 store apply --phase <phase>，最后 store verify 一次性总验）
  ↓
第 5 步：最终人工核对与提交（用户在浏览器中做最后检视并点击提交认证）
```

### 第 1 步：登录与应用名称保留协同
- Agent 先执行 `store launch` 拉起独立 Edge 浏览器（秒级返回）：
  ```powershell
  & "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
  ```
- Agent **必须向用户发送登录与应用命名指引消息并等待回复**：
  - 引导用户在弹出的 Edge 窗口中登录微软账号与 2FA；
  - 提示用户登录后点击「新产品」$\rightarrow$「MSIX 或 PWA 应用」，**由用户亲自输入应用名称并点击「保留产品名称」**；
  - **向用户提供建议产品名称与备选名**；
  - **说明若名称已被占用的解决办法**（如增加特色修饰词、副标题、开发者标识等）；
  - 明确告知用户：**保留成功后，请在聊天框回复我说『我保留好了』或『下一步』！**
- Agent 结束当前发言，从容等待用户回复。

### 第 2 步：上架材料全面盘点与来源协同（生成前必问）
- Agent **必须向用户全面列出 Microsoft Store 上架所需的全部材料清单与规格**：
  - **文案类（4项）**：完整应用描述（详细阐述核心价值）、一句话简短摘要、功能特性列表（3-5条）、搜索关键词（5-8个）；
  - **图像资产类（5项）**：
    - `1366 x 768` 桌面高清主屏幕截图（真实运行渲染）；
    - `50 x 50 / 300 x 300` 商店徽标（`StoreLogo.png`）；
    - `150 x 150` 开始菜单中磁贴（`Square150x150Logo.png`）；
    - `44 x 44` 任务栏小图标（`Square44x44Logo.png`）；
    - `310 x 150` 宽磁贴（`Wide310x150Logo.png`）；
  - **合规声明类（3项）**：主要类别（如 Productivity）、`runFullTrust` 纯本地离线使用理由、纯本地数据隐私策略文本。
- Agent **必须主动询问用户材料来源意向**：
  - **方案 A（由 Agent 全自动生成与设计）**：Agent 明确说明打算如何生成（真机渲染截取 1366x768 界面、主题设计 Logo、撰写文案）；
  - **方案 B（由用户亲自提供）**：用户自行提供设计素材；
  - **方案 C（混合模式）**：文案/截图由 Agent 生成，特定 Logo 由用户提供。
- Agent 等待用户确认方案后，再开始执行真实素材生成与整理。

### 第 3 步：真机素材生成、交互式弹窗与用户检视确认
- 严禁使用脚手架纯色占位图；Agent 基于真实应用渲染捕获高清截图并设计精致图标；
- Agent 将素材统一整理在 `store-submission-assets` 文件夹：
  - **所有文件名必须全部采用纯中文清晰命名，明确标注用途与分辨率规格**（如 `00_时光回忆录_完整文案与亮点特性说明.txt`、`01_微软商店详情页_主运行界面高清截图_1366x768.png` 等）；
  - **文字说明绝对禁止使用 md，一律采用 txt 格式**；
- 唤起本地文件管理器置顶展示给用户检视：
  - Windows：使用交互式任务指令拉起文件管理器，并通过 `OpenInputDesktop` 显式检测窗口句柄确保弹窗已置顶展示在用户屏幕前；
  - macOS：执行 `open <assetDir>` 呼出 Finder 窗口；
- Agent **向用户逐一说明每个素材的作用与规格，提醒用户核对确认**；
- 明确告知用户：**素材核对无误后，请在聊天框回复我说『确认素材』或『继续』，我接着为您全自动填报并上传！**
- Agent 结束当前发言，从容等待用户确认。

### 第 4 步：按需精准生效（Direct-Apply）与自动化接力
用户回复确认后，Agent 依据当前后台真实状态，**优先执行未完成模块的精准生效（`store apply --phase <phase>`），严禁对已有绿标的模块重复全量轮询**：

```powershell
# 1. 自动同步名称保留与 Identity 信息
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 2. 生产封装生成 Store MSIX 包
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store

# 3. 离线静态预检（校验 MSIX、manifest、素材尺寸与文案）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug

# 4. 发现或创建本次提交草稿
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

# 5. 单阶段精准填报（按需对未完成阶段执行直接填报）：
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase properties
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase packages
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase options

# 6. 冷加载总体验证（确认 6 个模块均为 Complete 绿标）：
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug
```

### 第 5 步：最终人工核对与提交
- 验证通过（6 大模块全绿标）后，**CLI 不会自动点击最终的“提交进行认证”按钮**；
- Agent 提示用户在已打开的 Edge 浏览器中做最后的人工核对并亲自点击「提交进行认证」。

---

## 4. 六大阶段表单直接填报（Direct-Apply）SOP 全解

### 阶段 1：定价和可用性 (availability)
1. **全量字段映射**：
   - 市场选择：`input[name="marketSelection"][value="true"]` $\rightarrow$ 全球所有市场（默认）；
   - 定价层级：`price-tier-selection[pricetierkey="Retail"] he-select input` $\rightarrow$ **必须直接定位内部 input 键入 `'0'` 并回车（`press('Enter')`）或点击文本为 `'0'` 的 `<he-option>`**，彻底消除“应为价格计划设置有效的价格”红字校验拦截，点亮保存按钮；
   - 免费试用：`select[aria-label="TrialType"]` $\rightarrow$ `string:NoTrial`；
   - 受众与可见性：`#radioDistribution_PublicAudience` 与 `#radioVisibility_Public`；
   - 组织批量分发许可：`#enterpriseonline_checkbox`（默认勾选）；
2. **保存按钮**：定位 `#saveButtonPricing` 或 `button[name="save_button"]`，等待保存完成。

### 阶段 2：属性与隐私 (properties)
1. **主要类别**：`select[name="CategorySelect"]`，选择 `Productivity`（生产率）或对应分类，派发 `change` 事件；
2. **隐私策略声明**：
   - `select[name="privacyPolicySelection"]`；
   - 纯本地单机选 `No`；全信任应用若被要求提供隐私策略，选择 `Yes` $\rightarrow$ 勾选 `#privacyPolicyText` 单选框 $\rightarrow$ 在展开的 textarea 中填入纯本地不收集数据的离线隐私声明；
3. **系统声明复选框**：`storage-checkbox`、`backups-checkbox`、`windows-checkbox` 默认勾选；
4. **保存按钮**：定位 `button[name="save_button"]` 并点击。

### 阶段 3：年龄分级问卷 (age-ratings)
1. **问卷类型选择**：定位应用类型单选框，选择 `2558`（实用程序、生产力或其他应用）；
2. **问卷逐项作答**：
   - 遍历所有单选题，默认选择 `No`（无暴力、无不良内容、不共享位置、不包含用户生成内容）；
   - 对覆盖有自定义样式的单选框，使用 `{ force: true }` 或 DOM `evaluate(el => el.click())` 触发；
3. **提交与评分生成**：点击提交按钮生成 IARC 评级证书，确认通过。

### 阶段 4：程序包上传 (packages) 与官方 makemsix 打包
1. **MSIX 物理对齐与官方 makemsix 工具链铁律**：
   - 微软云端 Partner Center 严苛依赖 64KB 块边界物理对齐与 `AppxBlockMap.xml` 的 `LfhSize` 精确匹配；
   - 严禁使用普通 ZIP 压缩库私自拼凑 MSIX（会导致云端报 `NotImplementedException: The method or operation is not implemented`）；
   - 必须使用微软官方跨平台 [`microsoft/msix-packaging`](https://github.com/microsoft/msix-packaging) 编译的原生 `makemsix pack -d <layoutRoot> -p <output.msix>` 或 Windows 下 `@microsoft/winappcli` 打包；
2. **自动清理残留故障包**：进入页面先检测并清理历史故障包（点击 Delete 按钮并确认弹窗）；
3. **大文件 CDP 注入**：对于 50MB+ 的 MSIX 安装包，**必须使用 CDP 会话 `DOM.setFileInputFiles` 注入**，规避 WebSocket 50MB 传输上限；
4. **全生命周期监听**：等待 Validating 转圈结束且无云端报错，自动勾选 `Desktop` 与 `future` 设备系列支持并保存草稿。

### 阶段 5：商店详情文案与资产 (listing)
1. **语言选择与表单直达**：进入中文（zh-cn）表单，若在表格页则精准匹配表格行链接点击进入；
2. **核心文案定位**：
   - 详细描述：`#description-required`（派发 `input` 与 `change` 事件）；
   - 简短摘要：`#shortDescription`（展开补充字段）；
   - 功能特性：`#feature-${i}`（依次填充 3-5 项功能亮点）；
   - 搜索词：`#search-terms he-select input`，填入关键词并回车；
3. **图像资产精准映射上传**：
   - 桌面截图：定位 `#panel-2 input[type="file"]`，上传 1366x768 高清截图；
   - 9:16 招贴画：定位 `.logo-upload-section:has-text("9:16 招贴画") input[type="file"]`，上传 720x1080 图像；
   - 1:1 酷图：定位 `.logo-upload-section:has-text("1:1 酷图") input[type="file"]`，上传 1080x1080 图像；
   - 300x300 应用磁贴：定位 `.logo-upload-section:has-text("300 x 300") input[type="file"]`，上传 300x300 图像；
   - 150x150 图标：定位 `.logo-upload-section:has-text("150 x 150") input[type="file"]`，上传 150x150 图像；
   - 71x71 图标：定位 `.logo-upload-section:has-text("71 x 71") input[type="file"]`，上传 71x71 图像；
4. **保存按钮**：点击保存并确保“需要至少一张屏幕截图”等红字校验消除。

### 阶段 6：提交选项 (options)
1. **发布模式**：选择 `Asap`（认证通过后立即发布）或 `Manual`；
2. **受限功能声明（runFullTrust 铁律）**：
   - 必须对 `<textarea class="text-area-width">` 执行**显式等待（`waitFor({ state: 'visible' })`）**，防止 Angular 异步渲染导致漏填；
   - 自动填入合规声明：“本产品为基于 Windows 本地独立运行的桌面应用程序。需要使用 runFullTrust 权限以读写本地用户数据存储文件，实现数据本地持久化保存，不依赖也不连接任何外部云端网络服务。”；
   - 派发 `input` 与 `change` 事件消除 `has-error` 拦截；
3. **保存与静态提示横幅甄别**：排除微软常驻黄色说明横幅（`我们在你的 Package.appxmanifest 文件中检测到...`），防止误判为报错。

---

## 5. 避坑指南与 20 项工程防御铁律（必背）

1. **应用名称保留人机边界**：必须由用户在独立 Edge 中亲自输入并点击保留，Agent 仅提供建议与方案；
2. **材料盘点与来源协同**：生成或整理素材前，必须向用户全面列清材料清单并确认来源意向；
3. **拒绝纯色与未卸载遮罩截图（`window.qam` Promise 契约铁律）**：
   - 真机截图渲染前必须在 Playwright 中通过 `addInitScript` 注入与业务完全一致的 `window.qam` 异步 Mock：
     - `appInfo` 必须是 `async () => ({ name, version })` 返回 Promise（以满足 `app.js` 中的 `.then()` 链式调用）；
     - `loadState` 必须返回与应用 `reactive` 模型字段完全一致的数据（如 `{ version: 1, items: [...] }`）；
   - 截屏前必须显式执行 `await page.waitForSelector('[v-cloak]', { state: 'detached', timeout: 5000 })` 确认 Vue 实例已完成挂载，并验证核心文字节点已呈现在页面上，**严禁在 `v-cloak` 遮罩未卸载时截取纯色或空背景**；
4. **全中文清晰命名与 txt 文本规范**：素材文件夹内文件必须全中文命名并注明用途分辨率；文字说明禁止使用 `.md`，一律使用 `.txt` 纯文本格式；
5. **单阶段精准生效**：优先执行 `store apply --phase <phase>` 单点填报，严禁全量重复轮询；
6. **打包 EBUSY 防御**：生产打包配置必须显式忽略 `.cache`、`build`、`out` 等运行时目录，防止复制独占锁定的 Edge profile；
7. **Web Components 穿透**：`<he-select>` 直接定位内部 input 回车生效，`<he-button>` 支持复合定位；
8. **`:visible` 可见性过滤铁律**：断言错误与警告时必须过滤 `:visible` 伪类，严禁抓取隐藏模板节点；
9. **保存按钮幂等判定**：保存按钮处于 `disabled` 状态时代表状态已收敛，安全放行无需超时点击；
10. **SPA 异步渲染防抖**：路由跳转后必须等待核心数据容器渲染就绪再读取；
11. **单选/复选遮挡强制触发**：Angular 单选框受上层 span 遮挡时，使用 `{ force: true }` 或 DOM 派发事件；
12. **50MB+ 大包 CDP 注入**：大安装包上传必须通过 CDP `DOM.setFileInputFiles`，绕过 WebSocket 传输上限；
13. **官方原生 `makemsix` 64KB 块对齐铁律**：严禁使用第三方普通 ZIP 压缩生成 MSIX，必须通过微软官方编译的原生 `makemsix` 工具构建；
14. **macOS 构建编译器探针防死锁**：CMake 构建时必须显式注入 `CC=/usr/bin/clang CXX=/usr/bin/clang++`，规避系统别名或脚本拦截；
15. **`runFullTrust` 受限功能显式等待**：「提交选项」页必须显式 `waitFor` 异步文本域并填入本地权限合规理由，消除 `has-error`；
16. **静态黄色说明横幅甄别**：保存断言必须排除包含“错误”字样的静态官方声明横幅（如 `我们在你的 Package.appxmanifest`）；
17. **属性页隐私策略声明**：全信任应用强制要求提供隐私策略时，选择填写离线声明文本（`#privacyPolicyText`）；
18. **完全就绪 DOM 调试**：报错排查必须显式等待所有 progressbar 彻底卸载，抓取全量真实 DOM 分析；
19. **严格串行执行与 CDP 独占锁防死锁**：严禁并发派发 Edge / Store 命令，单任务串行执行；
20. **全交互显式日志记录与禁绝控制台噪音**：所有浏览器交互必须打印清晰的 `[BROWSER_ACTION]` 结构化日志，严禁打印 `BROWSER_CONSOLE` 冗余日志；
21. **程序包故障行与删除态自愈（Packages Error Recovery）铁律**：在程序包上传页面，若因云端网络瞬态出现 `已暂停 (Paused)`、`错误 (Error)` 或 `This package will be removed (指示删除)` 状态时，必须自动识别并点击 `a[data-l10n-key="app_package_action_delete"]` 清理故障项，或点击 `a[data-l10n-key="app_package_action_revert"]` 恢复正常包状态，点亮保存按钮，严禁在故障项未清除时强行点击 disabled 的保存按钮；
22. **云端大包解包异步等待与刷新重载铁律**：对于 50MB+ 的安装包，微软 Partner Center 在云端解包验签通常需要 1~2 分钟。若提示“程序包需要长时间进行处理”，必须等待就绪或通过 `page.reload()` 重新同步云端真实就绪状态；
23. **本地持久化与数据模型全链路一致性铁律**：任何在 Mock、Renderer、Main 与 Store 之间流转的数据，字段名必须 100% 严格一致（如 `state.items`），严禁使用未对齐的字段格式。
