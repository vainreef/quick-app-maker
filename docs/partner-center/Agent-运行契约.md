# Microsoft Partner Center 商店发布 Agent 运行契约 (V2 标准版)

本文档是 Agent 执行 Windows 应用发布提审的**唯一最高行为准则与操作手册**。
任何 Agent 接手发布任务时，必须以本契约和 quick-app-maker/skills/vainreef-fast-publish/SKILL.md 为准，严禁单脚本盲目一冲到底，严禁盲目猜想。

---

## 一、 唯一官方工具链入口

执行所有发布、排障、诊断命令，一律且仅使用：
`powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action <action> [参数...]
`
- **源码工程**：quick-app-maker/toolchain/edge-store-cli/EdgeStore.Cli.csproj（基于 .NET 10 SDK 绿色版免安装编译）。
- **绝对禁止**：严禁执行任何 pps/Project/edge-store-cli-fast/ 废弃旧目录下的 DLL 或临时脚本。

---

## 二、 商店发布标准 10 步流水线（严禁跳步与一冲到底）

发布流程严格解耦为 **10 个离散阶段**。每个阶段执行后，必须通过 DOM 结构化证据确权成功后，方可进入下一阶段：

| 阶段 | 阶段标识 | 执行命令 | 核心任务与自检断言 |
| :---: | :--- | :--- | :--- |
| **STORE -1** | **名称预留与建项 (用户交互模式)** | -Action reserve -Manifest .\<app>\build\edge-store.json | 自动拉起弹窗，由用户在浏览器直接输入心仪名称并点击保留，脚本自动监听进入产品页后全自动抓取 3 大 Identity 并回填本地 |
| **STORE 0** | **离线静态预检** | -Action preflight -Manifest .\<app>\build\edge-store.json | 校验描述字数 (300~500字)、特性列表 ($\ge 3$)、关键词 ($\le 7$)、MSIX 及资产文件存在性 |
| **STORE 1** | **浏览器拉起确权** | -Action launch -KeepOpen | 拉起独立常驻 Edge (9222 调试端口)，确权已处于微软开发者后台登录态 |
| **STORE 2** | **提审探测与基线** | -Action discover -Manifest .\<app>\build\edge-store.json | 提取当前草稿 SubmissionId，生成 6 大表单直达 URL 并写入 checkpoint |
| **STORE 3** | **定价与可用性** | -Action step -Phase availability -Manifest .\<app>\build\edge-store.json -Apply | 设定免费 (Tier 0)、全市场分发，保存并断言概览页显示「完成」 |
| **STORE 4** | **应用属性 (含隐私)** | -Action step -Phase properties -Manifest .\<app>\build\edge-store.json -Apply | 分类设为工具，**强制声明包含隐私政策并填入离线文本**，保存并断言「完成」 |
| **STORE 5** | **年龄分级** | -Action step -Phase ageRatings -Manifest .\<app>\build\edge-store.json -Apply | 获取/关联 IARC 年龄分级问卷，保存并断言「完成」 |
| **STORE 6** | **程序包上传** | -Action step -Phase packages -Manifest .\<app>\build\edge-store.json -Apply | 上传 MSIX，自动清理异常包，等待后台唯一 Validated 验证通过并保存 |
| **STORE 7** | **Store 一览 (Listing)** | **分步执行**：<br>1. -Action cleanlanguages -Manifest ...<br>2. -Action filllisting -Manifest ... | **阶段1**：严格正则 languageid=5 清理 85 门外语并保存网格；<br>**阶段2**：自动展开折叠面板，自愈关键词至 $\le 7$ 个，CDP派发上传事件上传截图/图标并保存 |
| **STORE 8** | **提交选项 (Options)** | **分步执行**：<br>1. -Action inspectoptions -Manifest ...<br>2. 经用户批准后执行 -Action filloptions -Manifest ... | **阶段1**：等待受限功能 API 返回，提取 DOM 向用户汇报；<br>**阶段2**：选择发布模式 (Manual)，填报 unFullTrust 桌面进程理由并保存 |
| **STORE 9** | **概览页全绿校验** | -Action verify -Manifest .\<app>\build\edge-store.json | 冷加载 Overview 页面，断言 6 大模块 100% 全部处于「完成」状态 |
| **STORE 10** | **最终提交认证** | -Action submit -ConfirmSubmit -Manifest .\<app>\build\edge-store.json | 向用户展示全绿总览，经用户明确同意后触发最终提审 |

---

## 三、 七大核心防坑铁律与异常自愈规范

### 1. 隐私政策强制铁律 (Global Privacy Policy Invariant)
- quick-app-maker 生态下**所有 App 必须选择「是，包含隐私策略」(Privacy Policy: Yes)**；
- 隐私策略文本栏统一填入标准离线声明：
  本应用为本地运行工具，不收集、不存储、不上传任何用户个人隐私数据或使用习惯。
- 严禁在 manifest 中将隐私政策设为 No。

### 2. 语言网格清理严格正则定界 (Strict Regex Language ID Invariant)
- 进入 managelanguages 页面清理外语时，**必须使用严格正则定界符**：
  /(?:[?&])languageid=5(?:&|$)/ 或 /(?:[?&])languagecode=zh-cn(?:&|$)/i
- 绝对禁止使用子串 includes('languageid=5') 或 includes('5')，避免将 151、52、115、45、159 等外语错误保留导致残留 80+ 门语言。

### 3. 视觉资产上传必须派发 DOM 事件 (File Upload Event Dispatch Invariant)
- CDP 命令 DOM.setFileInputFiles 仅在 <input type=file> 节点上挂载文件列表，**不会触发 Angular 上传监听器**；
- 驱动必须在设置文件后立即执行：
  `javascript
  input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
  input.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  `
- **保存前硬核断言**：在点击保存前，必须校验页面上 桌面 (1) 或缩略图已成功生成。未生成截图绝对禁止点击保存！

### 4. 关键词（Tag）容量自愈与折叠面板展开 (Keyword Accordion & Limit Invariant)
- 微软商店硬性限制：**关键词最多 7 个**；
- #search-terms 处于 其他信息 折叠区域内，执行前必须先点击展开按钮；
- 必须先读取现场已存 Chip，定位非目标或超出配额的旧标签并点击关闭图标移除，最后仅追加缺失词汇，确保页面标签总数 $\le 7$ 个。

### 5. 提交选项受限功能异步等待 (Restricted Capabilities Async Invariant)
- MSIX 声明了 unFullTrust 时，页面中的 <section>受限的功能</section> 是由 Angular 异步拉取后台接口后动态渲染的；
- 必须使用 Waiter.RequireAsync 显式等待 	extarea.text-area-width 渲染出现后再取值/填报；
- 填入理由必须通过属性描述符 Setter 写入并触发 input/change。

### 6. 安全导航与绝对 URL 定位 (Absolute URL Navigation Invariant)
- 严禁通过页面上的模糊相对链接（如  [href*=/overview]）返回概览页（防止误跳微软外部 Learn 文档）；
- 必须使用由 ${baseUrl}//overview 显式构造的绝对控制台 URL 导航。

### 7. 干净编译与 DLL 锁处理 (Clean Build & Compilation Invariant)
- 每次修改 C# 驱动代码后，建议使用 -p:UseSharedCompilation=false 进行编译；
- 若遇文件时间戳或增量编译未命中，先清理 bin/ 与 obj/ 目录再执行构建。

### 8. MSIX DisplayName 与商店预留名称强一致性铁律 (MSIX DisplayName Matching Invariant)
- **微软后台强制校验**：MSIX 的 `Package.appxmanifest` 中的 `<Properties><DisplayName>` 以及 `<Application ... uap:VisualElements DisplayName="...">`，**必须与微软开发者后台该产品已预留的名称 100% 完全一致**；
- 若预留了 `Qiangua - 牵挂桌面记事与倒数日`，本地清单绝不能只写短名称“牵挂”，否则微软后台上传时会直接报错拒包：`此软件包的清单（Package/Properties/DisplayName）使用了你未保留的显示名称`；
- **自动化回填原则**：在 STORE -1 用户预留名称成功后，驱动必须将从后台抓取到的实际完整产品名称，自动同步回填至 `edge-store.json` 与 `Package.appxmanifest`，然后再触发 Release 编译与 MSIX 打包。

### 9. 程序包上传实时负反馈与异常自动清理铁律 (Packages Realtime Error Trap & Auto-Clean Invariant)
- **拒绝单向死等**：包上传验证轮询期间，每 500ms 必须同时监听 `.alert-error`、`.alert-danger`、`.faulty-package-message` 等负反馈报警；
- **即时秒级中断**：一旦发现微软后台报错（如“未保留的显示名称”或“包验证错误”），必须立即提取具体错误文本，自动点击 `Delete` 清理异常包，并秒级抛出异常中断，绝不盲等 12 分钟超时；
- **上传前环境自愈**：每次上传前，自动扫描页面是否存在残留的 Faulty Package 并自动清理。

---

## 四、 退出状态与结构化证据输出

每次执行完毕，Agent 必须向用户输出包含以下字段的清晰结构化状态表：
1. 当前阶段与执行命令；
2. 页面 URL 与 DOM 探针状态；
3. 6 大模块现场真实完成情况；
4. 退出码（0 = 成功，非 0 = 异常并附带报错根因）。
