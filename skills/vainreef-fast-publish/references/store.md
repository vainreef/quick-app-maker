# Partner Center V2 自动化发布手册

## 图片交付与用户确认

- 图片由 Agent 生成、收集、重命名和整理，是否符合预期由用户最终判断。
- 用户表示图片不合格时，立即返回调整并再次交由用户检查。
- 模型缺少图片读取能力时，直接打开素材文件夹，不做视觉评价。
- 文件存在性和上传动作属于工具流程；视觉质量不由自动检查代替。
- 素材统一放入 `store-submission-assets`，使用中文文件名标明用途和尺寸，文案说明使用 `.txt`。

## 0. 账号、证书与浏览器常识

- **浏览器驱动机制**：自动化发布采用**宿主机内置独立浏览器实例**（Windows 优先使用 Edge，macOS/Linux 自动兼容 Chrome 与 Edge），通过 Chrome DevTools Protocol (`connectOverCDP`) 由 Playwright 控制，使用工作区独立 profile 沙箱（`.cache/store`），不读取、不影响用户日常浏览数据；
- **开发者账号**：微软账号与个人开发者认证免费，无需高昂费用；若用户未注册或未认证，运行 `store launch` 自动拉起浏览器，指引用户在网页中完成注册或个人认证；
- **代码签名**：上架微软商店的 MSIX 包由**微软商店官方在云端自动完成安全签名**（Store Signing），**完全不需要开发者购买或提供第三方代码签名证书**，严禁向用户询问证书问题！

### 0.1. `product/NotFound` 故障根因与诊断恢复 SOP（铁律）
- **现象**：浏览器跳转到 `https://partner.microsoft.com/zh-cn/dashboard/product/NotFound?locationHref=...`，页面显示“抱歉，我们找不到该页”。
- **三大本质原因**：
  1. **未登录或 Token 失效（最普遍）**：`store launch` 启动的是独立全新沙箱环境，没有日常浏览器的 Cookie。微软前端路由对未鉴权会话直接访问具体产品详情页时，为了防止外部恶意探测 Product ID，不会跳转到登录页，而是**直接安全脱敏重定向至 `NotFound` 页面**；
  2. **租户 / 账号错位（Tenant Mismatch）**：用户在浏览器中登录了个人微软账号（MSA），而该应用是在 Azure AD / Entra ID 组织工作租户下创建的（或反之）；或者登录的账号 A 与拥有该产品的开发者账号 B 不一致；
  3. **产品名尚未完成最终保留或已被释放**：在保留名称时仅输入了文字但未点击「保留产品名称」，或者保留期满未提交已被云端释放。
- **排查与恢复 SOP（20 秒恢复）**：
  1. 提示用户在当前独立浏览器右上角点击 **「登录」**（或直接访问 `https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview`）；
  2. 确认登录的微软账号拥有开发者权限，并在右上角检查租户切换；
  3. 在「应用和游戏」大厅查看列表中是否有该产品：
     - 若有：直接在列表中点击进入；
     - 若无：点击「新产品」$\rightarrow$「MSIX 或 PWA 应用」输入名称重新保留。

### 0.2. CDP 自动化凭据嗅探原理与用户透明度契约（人机沟通铁律）
- **自动化工作原理**：
  - `store launch` 启动浏览器时通过 `--remote-debugging-port` 开启了本地安全调试管道（CDP）；
  - 当用户在网页上点击「保留产品名称」后，Partner Center 自动跳转至产品概览页，URL 地址栏携带分配好的 12 位 ProductId（如 `9N3LCK2GWVL7`），页面标题显示应用中文名称；
  - 用户回复“我保留好了”后，脚本通过本地调试端口向浏览器读取当前激活 Tab 的 URL 与 Title，并自动跳转至 `/identity` 页面抓取官方 `Package/Identity/Name` 与 `Publisher`，回填进 `Package.appxmanifest` 与 `desired-state.json`。
- **透明度沟通铁律（防疑虑）**：
  - 在第 1 步向用户发送登录指引时，**必须提前向用户告知该自动化机制**：
    > “在保留成功后，工具会自动通过浏览器本地通道读取新生成的 Product ID 与微软官方开发者凭据并回填工程，无需您手动查找和复制任何繁琐的代码！”
  - 严禁在未做任何机制解释的情况下直接贴出抓取的 ID，防止用户产生“我没告诉你任何信息，你怎么拿到的”黑盒恐慌。

### 0.3. 「产品草稿」与「提交版本草稿」两级生命周期（`Start_Submission` 铁律）
- **两级生命周期界限**：
  - **Level 1：产品草稿态 (Product Draft)**：仅在云端占坑保留了名称和 Product ID，页面显示“处于草稿状态”，此时**尚未生成版本提交草稿**，页面上只有基础信息与一个醒目的 **「开始提交」** 按钮（`<he-button data-l10n-key="Start_Submission">`），此时不存在 6 个阶段的填报路由；
  - **Level 2：提交版本草稿态 (Submission Draft)**：必须显式触发「开始提交」后，云端才会生成 `Submission 1: 正在草拟`，并在页面上渲染出 6 大模块卡片与各自对应的子路由。
- **发现机制契约**：
  - `store discover` 必须优先检查当前页面是否包含 `Start_Submission` 按钮；若处于产品概述页且无 submissionId，必须主动点击该按钮完成初始提交创建，等待 6 大模块行加载完毕后再提取真实 `submissionId` 与 routes。

### 0.4. CLI 进程确定性退出与工作区锁防死锁（`process.exit` 铁律）
- **根因分析**：Playwright 通过 CDP 连接 Chrome/Edge 时，Node.js 底层 libuv 事件循环可能存在未及时清理的网络句柄。如果 CLI 脚本仅设置 `process.exitCode` 而未显式调用 `process.exit()`，会导致进程驻留变成僵尸进程（Zombie CLI）。
- **死锁后果**：该僵尸进程会持续持有工作区锁（`app-slug.lock`），导致后续任何命令都报 `workspace is busy (pid xxx)`。
- **规范铁律**：
  - 业务代码在 `dispatch` 之后必须显式调用 `process.exit(code ?? 0)`；
  - 遇到 `workspace is busy` 报错时，排查该 PID 是否属于已输出 PASS 的前序任务，确认后安全清理锁文件。

### 0.5. 外置驱动器与跨工程工具链解耦规范 (`QAM_TOOLCHAIN_ROOT` 铁律)
- 当被操作的 App 位于外部挂载卷（如移动硬盘 `/Volumes/...`）或非 quick-app-maker 目录下时：
  - `bin/qam.mjs` 会自动将该 App 的父目录解析为 `workspace`，防止抛出 `app is outside WORKSPACE_ROOT`；
  - 必须同时导出 `QAM_TOOLCHAIN_ROOT` 与 `QAM_ENGINE_ROOT`，确保打包脚本能准确寻址微软原生 `makemsix` 编译器与运行时模板。

## 1. 微软商店发布五步人机协同标准流程

```text
第 1 步：登录与应用名称保留协同 (store launch -> 用户在浏览器中亲自点击保留 -> store reserve 回填)
  ↓
第 2 步：上架材料全面盘点与来源协同 (向用户列出文案、图片规格，确认由 Agent 生成还是用户提供)
  ↓
第 3 步：真机素材生成、交互式弹窗与用户检视确认 (纯中文命名、纯文本 txt、置顶展示并等待用户回复确认)
  ↓
第 4 步：按需精准生效与全自动流水线 (优先单阶段 store apply --phase，最后 store verify 一键总验)
  ↓
第 5 步：最终人工核对与提交 (用户在已打开的浏览器中复核并亲自点击「提交进行认证」)
```

## 2. 自动化流水线标准指令（严禁跳步或颠倒时序）

```powershell
# ==================== Windows (PowerShell) ====================
# 1. 启动独立浏览器引导用户登录/注册 Partner Center (秒级返回)
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
# -> 用户在浏览器中登录并亲自保留名称后，在聊天框回复「我保留好了」

# 2. 自动化同步应用名称与回填 Identity 信息到 manifest
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 3. 生产封装 MSIX（必须在 reserve 之后执行，跨平台生成 64KB 对齐包）
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store

# 4. 离线静态预检（校验 MSIX、manifest、素材尺寸与文案）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug

# 5. 发现当前提交草稿路由
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

# 6. 单阶段精准直接填报 (Direct-Apply，按需填报未完成模块)
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase properties
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase packages
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase options

# 7. 执行现有总检（确认 6 个模块均为 Complete 绿标）
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug

# ==================== macOS / Linux (终端 Bash / Zsh) ====================
# node bin/qam.mjs store launch --app /path/to/app-slug
# node bin/qam.mjs store reserve --app /path/to/app-slug --name "应用名称"
# node bin/qam.mjs package /path/to/app-slug --profile store
# node bin/qam.mjs store preflight --app /path/to/app-slug
# node bin/qam.mjs store discover --app /path/to/app-slug
# node bin/qam.mjs store apply --app /path/to/app-slug --phase <phase>
# node bin/qam.mjs store verify --app /path/to/app-slug
```

## 3. 页面自动化操作与收敛判定

- **Playwright 定位器**：首选 `getByRole`、`getByLabel`、`getByText`，默认穿透 open Shadow DOM；
- **Web Components 支持**：`<he-select>` 内部直接键入回车生效，`<he-button>` 支持复合定位；
- **大包上传支持**：50MB+ 大文件通过 CDP `DOM.setFileInputFiles` 注入；
- **单阶段执行链**：`PageKind → Direct-Apply（完整填表）→ 保存收敛 → 记录检查点`；
- **总检**：六个阶段处理完成后运行现有 `store verify`，不把文档中尚未执行的其他机制写成已完成事实；
- **人工终审边界**：`store verify` 全绿标后，CLI 不会自动点击最终的“提交进行认证”按钮，留给用户在浏览器中复核并点击。


## 4. 六大阶段表单直接填报细节

### 阶段 1：定价和可用性 (availability)
1. **全量字段映射**：
   - 市场选择：`input[name="marketSelection"][value="true"]` $\rightarrow$ 全球所有市场（默认）；
   - 基准币种（Base Currency）：`.price-config > he-select` $\rightarrow$ **必须优先在内部 input 填入目标币种（如 `CNY` 或 `USD`）并回车**；若未选择币种，右侧价格层级控件会被系统级联锁定为 `disabled=""` 并抛出“没有为可购买的产品创建 PriceSchedule”；
   - 定价层级：等待 `price-tier-selection` 解除 `disabled` 后，定位 `price-tier-selection[pricetierkey="Retail"] he-select input` $\rightarrow$ **直接定位内部 input 键入 `'0'` 并回车（`press('Enter')`）或点击文本为 `'0'` 的 `<he-option>`**，彻底消除“必须为此产品配置价格”红字校验拦截，点亮保存按钮；
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
2. **六大精简核心字段填报**：
   - **说明**：`#description-required` $\rightarrow$ 详细介绍正文（写个几百字），自动派发 `input` 与 `change` 事件；
   - **简短描述**：`#shortDescription` $\rightarrow$ 200 字以内的精炼核心卖点；
   - **产品功能**：依次填满页面预置的前 3 个功能条目（`#feature-0`、`#feature-1`、`#feature-2`）；
   - **关键字**：`#search-terms he-select input` $\rightarrow$ 选上 3~5 个搜索关键词，逐个回车添加；
   - **桌面截图**：定位 `#panel-2 input[type="file"]`，上传 1~4 张软件操作界面截图（1366x768 或更高），消除“需要至少一张屏幕截图”红色警告横幅；
   - **1:1 酷图**：定位 `.logo-upload-section:has-text("1:1 酷图") input[type="file"]`，上传 1080x1080 高清图标，作为 Store 布局的核心主徽标；
3. **附加美工与促销画（Additional Artwork / Xbox / 预告片）说明**：
   - `#AdditionalArtwork-...` 和 `#promoImagesContainer` 属于预告片视频与 Xbox 主机游戏选填推广区，桌面纯单机应用全部留空，不产生任何拦截；
4. **保存按钮**：定位 `button[name="save_button"]`，点击保存完成该阶段。

### 阶段 6：提交选项 (options)
1. **发布暂缓选项（发布模式）**：
   - 定位 `input#radioReleaseDate_asap`（认证通过后立即发布，默认）或 `input#radioReleaseDate_manual`（手动发布），派发事件选中；
2. **受限功能声明（runFullTrust 铁律）**：
   - 必须对 `<textarea class="text-area-width">` 执行**显式等待（`waitFor({ state: 'visible' })`）**，防止 Angular 异步渲染导致漏填；
   - 自动填入合规声明：“本产品（{AppName}）为基于 Windows 本地独立运行的桌面应用程序。需要使用 runFullTrust 权限以读写本地用户数据存储文件，实现数据本地安全持久化保存，不依赖也不连接任何外部云端网络服务。”；
   - 派发 `input` 与 `change` 事件消除 `has-error` 拦截；
3. **认证说明与提交通知**：
   - 如有需要可填写测试说明；通知受众按默认设置；
4. **保存草稿**：排除微软常驻黄色说明横幅（`我们在你的 Package.appxmanifest 文件中检测到...`），定位 `button[data-l10n-key="optionsSave"]` 或 `button.btn-primary` 保存。


## 已验证场景的故障恢复经验

1. **应用名称保留人机边界**：必须由用户在独立 Edge 中亲自输入并点击保留，Agent 仅提供建议与方案；
2. **材料盘点与来源协同**：生成或整理素材前，必须向用户全面列清材料清单并确认来源意向；
3. **原生无头截图与 Promise 契约铁律（`qam screenshot` 命令）**：
   - 严禁向操作系统申请物理桌面截屏权限（严禁加载所谓“桌面操作技能”）；
   - 统一使用原生命令 `qam screenshot .\app-slug --width 1366 --height 768 --output .\app-slug\store-submission-assets\01_应用主界面高清截图_1366x768.png`；
   - 工具内置了符合 `window.qam` 异步 Promise 契约的 Proxy Mock，并自动断言 `v-cloak` 彻底脱落与 DOM 渲染稳定后捕获 1366x768 高清 PNG，零依赖、零系统权限、确定性生成；严禁截取纯色或未激活的空背景；
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
23. **本地持久化与数据模型全链路一致性铁律**：任何在 Mock、Renderer、Main 与 Store 之间流转的数据，字段名必须 100% 严格一致（如 `state.items`），严禁使用未对齐的字段格式；
24. **表单 SPA 瞬态加载错误自愈（ng-hide 与页面加载重试机制）**：在各 phase 填报中，若保存按钮存在但处于不可见状态（如父容器包含 `ng-hide` 且显示“我们在加载此页面时遇到问题。请刷新页面或在几分钟后重试”），这是 Partner Center 内部微前端服务的瞬态网络异常；严禁盲目判定为“保存按钮丢失”，必须执行 `page.reload({ waitUntil: 'domcontentloaded' })` 重新挂载表单；
25. **CLI 进程确定性退出与工作区锁清理**：自动化 CLI 在完成所有 Playwright CDP 操作后必须显式调用 `process.exit(code)` 彻底释放 libuv 事件循环中的活跃网络句柄，防止进程驻留持有 `WorkspaceLock` 引发下一次执行的 `workspace is busy` 死锁；
26. **全套商店提审资产规格清单（非占位图）**：必须使用真机高清截图与图标，全套包含：
    - `00_完整文案与亮点特性说明.txt`（包含几百字详细说明、200字精炼卖点、3条产品功能、3~5个搜索词）；
    - `01_应用主界面高清截图_1366x768.png`（传 1~4 张软件操作界面截图）；
    - `02_1比1酷图主徽标_1080x1080.png`（软件图标放大成 1080x1080 作为 Store 主徽标）；
    - `03_MSIX程序包全套图标集合`（50x50、150x150、44x44、310x150、300x300、71x71 等）；
    - `04_runFullTrust权限理由与离线声明.txt`；
    - `05_隐私策略声明说明.txt`；
27. **两级提交草稿生命周期管理**：在 `store discover` 中，新创建的产品仅有“产品草稿态”，必须定位并触发 `<he-button data-l10n-key="Start_Submission">`（开始提交）生成 `Submission 1` 后方可获取 6 大模块的完整填报路由；
28. **收敛比对与价格阶层 Fail-Fast 铁律**：定价与可用性阶段必须严格匹配选中价格阶层（如 `0 免费`），价格下拉框为空时绝对严禁判定为匹配收敛；未选中有效阶层时底层自动化强制抛错阻断，保存后必须做表单字段二次回读确认，杜绝未选价格报告 Complete 的假阳性事故；
29. **视觉自查第一铁律与无侵入多视图截图**：初版开发完成后必须通过 `qam screenshot` 进行核心交互按钮是否可见、深浅背景文字对比度是否清晰、界面是否无残留技术调试字样的视觉自查；非首屏截图统一使用 `qam screenshot .\app-slug --eval "..."` 或 `--click "..."`，绝对严禁为截图而临时篡改业务源码；
30. **文案单一真理源自动同步机制**：`store-submission-assets/00_*.txt` 由底层 `loadDesired` 自动嗅探解析并回填至 `desired-state.json`，确保自动填报所使用的就是用户确认的正式文案，严禁上传脚手架占位文案；
31. **告警零容忍阻断铁律**：日志中凡出现 `failed`、`not found`、`has-error`、`REQUEST_FAILED`，即使退出码为 0，也一律视为存在潜在风险，必须主动就地核实与排查；
32. **阶段切换工作区锁协同**：进入 `store launch` / `store apply` 发布流程前，必须先停止后台正在运行的 `qam dev` 任务，释放工作区互斥锁（`WorkspaceLock`），避免触发 `workspace is busy`。

