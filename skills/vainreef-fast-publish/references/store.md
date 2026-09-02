# Partner Center V2 自动化发布手册

## 图片交付与用户确认

- 图片由 Agent 生成、收集、重命名和整理，是否符合预期由用户最终判断。
- 用户表示图片不合格时，立即返回调整并再次交由用户检查。
- 模型缺少图片读取能力时，直接打开素材文件夹，不做视觉评价。
- 文件存在性和上传动作属于工具流程；视觉质量不由自动检查代替。
- 素材统一放入 `store-submission-assets`，使用中文文件名标明用途和尺寸，文案说明使用 `.txt`。

## 0. 账号、证书与浏览器常识

- **Edge 浏览器驱动机制**：自动化发布采用**宿主机内置 Microsoft Edge 独立实例**（Mac 环境自动兼容 Edge/Chrome），通过 Chrome DevTools Protocol (`connectOverCDP`) 由 Playwright 控制，使用工作区独立 profile 沙箱（`.cache/qam/session`），不读取、不影响用户日常浏览数据；
- **开发者账号**：微软账号与个人开发者认证免费，无需高昂费用；若用户未注册或未认证，运行 `store launch` 自动拉起 Edge 浏览器，指引用户在网页中完成注册或个人认证；
- **代码签名**：上架微软商店的 MSIX 包由**微软商店官方在云端自动完成安全签名**（Store Signing），**完全不需要开发者购买或提供第三方代码签名证书**，严禁向用户询问证书问题！

## 1. 微软商店发布五步人机协同标准流程

```text
第 1 步：登录与应用名称保留协同 (store launch -> 用户在 Edge 中亲自点击保留 -> store reserve 回填)
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
# 1. 启动独立 Edge 引导用户登录/注册 Partner Center (秒级返回)
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
# -> 用户在 Edge 中登录并亲自保留名称后，在聊天框回复「我保留好了」

# 2. 自动化同步应用名称与回填 Identity 信息到 manifest
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 3. 生产封装 MSIX（必须在 reserve 之后执行）
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
23. **本地持久化与数据模型全链路一致性铁律**：任何在 Mock、Renderer、Main 与 Store 之间流转的数据，字段名必须 100% 严格一致（如 `state.items`），严禁使用未对齐的字段格式。
