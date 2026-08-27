# Microsoft Partner Center 注册与商店表单提审全流程实战指南

> **文档定位**：本文档为 Agent 协助用户从零完成 **“微软开发者账号注册认证”**、**“Partner Center 建项与包身份配置”** 以及 **“6 大提审表单逐项填报”** 的官方权威实战手册。
> **核心原则**：当用户明确表达想上架商店时，Agent 必须**严格参考本文档的表单规范、标准推荐值与避坑技巧**，全流程陪伴并协助用户填表与提交。

---

## 阶段一：账号注册与个人开发者认证（Onboarding 向导）

### 第 1 步：创建 / 绑定微软账号
- **入口网址**：`https://signup.live.com/signup`
- **操作方式**：支持使用个人常用邮箱（如 Gmail、QQ、网易邮箱等）直接注册绑定，无需单独新建 `@outlook.com` 邮箱。
- 💡 **实测避坑点**：若网页端注册频繁提示“真人验证码异常”卡死，直接在 Windows 本机自带的 **Xbox 应用** 内点击注册，可秒级通过。

### 第 2 步：进入 Microsoft Store 开发人员入口
- **入口网址**：`https://storedeveloper.microsoft.com/`
- **操作方式**：登录微软账号后，系统会自动跳转至 onboarding 页面：`https://storedeveloper.microsoft.com/zh-Hans/onboarding`。

### 第 3 步：选择账户类型 ——「个人开发者」
- **选项对比**：
  | 账户类型 | 费用 | 适用对象 | 审核要求 |
  | :--- | :--- | :--- | :--- |
  | **个人开发者 (Individual)** ✅ | **免费** | 独立开发者、学生、业余爱好者 | 仅需个人身份证件认证 |
  | **公司账户 (Company)** | 免费 / 需资质 | 企业、商业团队 | 需企业域名工作邮箱、邓白氏编码等 |
- **Agent 指引**：建议用户选择 **「个人开发者」**（免费、流程最简捷）。

### 第 4 步：上传身份证件实名认证
- **操作流程**：核对姓名无误后，按页面提示上传本人**有效身份证件照片**并点击“继续”；
- **结果**：系统审核通过，完成实名认证。

### 第 5 步：完善个人资料与设定发布者名称
- **操作流程**：填写国家/地区、联系地址及邮箱；
- **设定发布者显示名称 (Publisher Display Name)**：如 `vainreef`（此名称即为开发者的对外品牌名，将公开显示在 Microsoft Store 的应用详情页上）；
- **完成**：点击提交，个人开发人员账户创建成功。

---

## 阶段二：全自动建项与提取核心包身份 (Store -1)

本阶段由 Edge Store CLI 驱动全自动完成，**严禁向用户提出任何控制台操作要求或输出手动点击步骤**。Agent 只需执行命令：
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action reserve -AppName "<应用名称>" -Manifest .\<app>\build\edge-store.json
```

驱动内部 CDP 自动化执行标准（仅供驱动内部实现参考）：
1. 自动导航至 Partner Center 控制台，在 DOM 定位 `+ 新产品` (`[data-automation-id="create-new-Application"]`)；
2. 自动填入应用名称，触发 `检查可用性` 校验并断言通过；
3. 自动点击 `保留产品名称 (Reserve product name)` 完成建项；
4. 自动跳转至 `/identity` 提取 `Package/Identity/Name`、`Package/Identity/Publisher` 与 `PublisherDisplayName`；
5. 自动将 3 项参数回填至项目源码 `Package.appxmanifest` 与 `edge-store.json`。

---

## 阶段三：商店 6 大表单全自动声明式填报 (Store 0~7)

本阶段由 Edge Store CLI 编排器全自动执行，**严禁人工在网页中逐项填报**。Agent 只需执行：
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
```

编排器将全自动根据 `edge-store.json` 声明式收敛以下 6 大表单模块：

---

### 表单 1：定价和可用性 (Pricing and Availability)
- **页面 URL**：`.../submissions/<submission-id>/availability`
- **Agent 推荐填报标准**：
  1. **市场 (Markets)**：选择 **「全球所有市场（包含全部 240+ 市场）」**（默认推荐）；
  2. **可见性 (Visibility)**：
     - 受众选择：**「开放受众 (Public audience)」**；
     - 可发现性选择：**「可用且可发现 (Make this product available and discoverable in the Store)」**；
  3. **排期计划 (Schedule)**：
     - 发布日期：选择 **「尽快 (As soon as possible)」**；
     - 停止购置：选择 **「永不 (Never)」**；
  4. **基本价格 (Base Price)**：
     - 在 Default 市场组的基本价格区域，货币选择 **「CNY - 中国」**，价格段选择 **`0`（¥0）**；
     - ⚠️ **技术细节**：价格段是 Angular 自定义组件（`he-select`），必须通过鼠标原生点击展开后点击 `0` 选项，方可使保存按钮亮起（`save=ENABLED`）。
  5. **保存**：配置完成后，底部的 **「保存草稿」** 按钮激活生效，点击保存。

---

### 表单 2：属性 (Properties)
- **页面 URL**：`.../submissions/<submission-id>/properties`
- **Agent 推荐填报标准**：
  1. **类别和子类别 (Category & Subcategory)**：
     - 主类别：根据应用性质选择（如：`生产率 Productivity` 或 `实用工具 + 工具 Utilities`）；
     - 子类别：选择对应的细分项；
  2. **隐私策略 (Privacy Policy)**：
     - **推荐模式 A（纯离线应用/无用户数据收集，最快通过）**：
       - 下拉框选择 **「否，我的产品不使用任何个人信息」**；
       - ⚠️ **实测铁律**：选「否」时下方**绝不存在任何输入框或文本域**，切勿试图寻找或填写不存在的 textarea，直接保存即可。
     - **模式 B（声明使用个人信息或需提供合规声明文本）**：
       - 下拉框选择 **「是，我的产品使用个人信息」**；
       - 下方单选框必须点击 **「提供隐私策略文本」**，此时页面会动态展开 `<textarea>`，填入本地隐私政策声明文本后，“保存”按钮才会高亮激活。
  3. **产品声明 (Product Declarations)**：
     - 勾选：`storage`（本地数据存储）、`windows`、`backups`（本地备份支持）；
     - ⚠️ **避坑警示**：若应用未接入端侧 AI 模型，**绝对不要勾选 `usesGenAI`**，防止被审核要求提供 AI 演示与合规证明；
  4. **系统要求 (System Requirements)**：
     - 硬件与平台：选择 Windows 10/11 Desktop；
     - 架构支持：勾选 `x64`（若支持 Arm64 同步勾选）；
  5. **保存**：点击页面右侧的 **「保存」** 按钮。

---

### 表单 3：年龄分级 (Age Ratings)
- **页面 URL**：`.../submissions/<submission-id>/ageratings`
- **Agent 推荐填报标准**：
  1. **调查表模式**：选择 **「我已准备好填写 IARC 调查表」**（或通用问卷模式）；
  2. **应用类型**：选择 **「其他所有应用类型 (All Other Application Types，value=2558)」**（切勿选游戏或社交通信类，避免冗余问卷）；
  3. **物理介质分发**：选择 **「否 (No)」**；
  4. **后续 9 项敏感内容问卷**（#1152-#1197，暴力、粗俗言语、色情、恐怖、毒品、位置共享、用户交流、数字商品、新闻教育等）：**全部选择「否」**；
  5. **预览与条款**：点击 **「预览分级」** 进入 `/summary` 汇总页，**必须勾选 IARC 使用条款同意框**，点击 **「保存」** 完成该模块。

---

### 表单 4：程序包 (Packages)
- **页面 URL**：`.../submissions/<submission-id>/packages`
- **Agent 推荐填报标准**：
  1. **构建商店包与资源质检**：Agent 执行本地打包，必须确保 `Assets/*.png`（StoreLogo、Square150x150 等）完整拷贝入发布目录：
     ```powershell
     dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
     # 确保 Assets 完整打包，避免 Partner Center 报图像缺失
     if (Test-Path ./Assets) { Copy-Item -Recurse -Force ./Assets ./publish/ }
     New-Item -ItemType Directory -Force ./store-package | Out-Null
     winapp package ./publish --executable <AppName>.exe --publisher "<Publisher>" --generate-cert --output ./store-package/<Identity>_<Version>_x64.msix
     ```
  2. **上传程序包**：驱动通过 Shadow DOM 穿透自动绑定 `.msix` 文件；
  3. **排障与保存铁律**：页面上**只能保留唯一一行状态为 `Validated` 的有效包**；若有历史残留的 `Analyzing` 或 `Error` 行，必须先点击 Delete / Cancel 清理干净；刷新页面（冷加载）确认 Save 按钮高亮后点击保存。
  4. **设备系列可用性 (Device family availability)**：
     - 必须且仅勾选：**「Windows 10/11 Desktop」**（取消勾选 Mobile/Xbox/Team/MixedReality）；
     - 勾选：**「让 Microsoft 决定是否向未来的设备系列提供此应用」**；
  4. **避坑提示**：
     - 模板默认若声明 `Windows.Universal` 会导致多设备矩阵全 rank 1 报错，必须在源码清单中删除 Universal 声明，只保留 `Windows.Desktop`；
     - 程序包页出现的 `runFullTrust` 警告属于桌面应用常态，无需人工申请。
  5. **保存**：上传并校验通过后，点击底部的 **「Save」** 按钮。

---

### 表单 2：属性 (App Properties)
- **页面 URL**：`.../submissions/<submission-id>/properties`
- **Agent 推荐填报标准**：
  1. **类别**：一级分类通常选择 **「工具和生产力」(Developer / Utilities / Productivity)**；
  2. **隐私策略（⚠️ 全局强制铁律）**：
     - `quick-app-maker` 生态下**所有 App 必须选择「是，包含隐私策略」(Privacy Policy: Yes)**；
     - 隐私策略文本栏中填入本地离线声明标准文本：
       `本应用为本地运行工具，不收集、不存储、不上传任何用户个人隐私数据或使用习惯。`
  3. **网站与支持联系信息**：按需选填；
  4. **保存**：点击底部的 **「保存」** 按钮。

---

### 表单 5：Store 一览 (Store Listing & 语言物料)
- **页面 URL**：`.../submissions/<submission-id>/managelanguages?producttype=app` 与 `.../listings?languageid=5&languagecode=zh-cn`
- **分步闭环工作流标准**：
  1. **阶段 1：语言网格检测与多余外语清理**：
     - 进入 `managelanguages` 页面，提取当前所有语言列表；
     - **严格正则匹配**：语言 ID 识别必须使用严格正则 `/(?:[?&])languageid=5(?:&|$)/`，绝对禁止使用模糊的 `includes('languageid=5')`，避免将 `151`、`52`、`115`、`45` 等外语错误保留；
     - 逐行点击非中文外语的【删除】按钮；
     - 点击页面底部的【保存】按钮提交语言网格变更（页面会自动重定向回概述页）。
  2. **阶段 2：进入中文（中国）详情表单**：
     - 导航进入 `.../listings?languageid=5&languagecode=zh-cn`；
     - 等待 `#description-required` 与 `#shortDescription` 渲染就绪；
  3. **文案物料填报**：
     - **完整描述 (Description)**：排版工整（300~500字），涵盖产品亮点、使用场景、设计理念与本地安全说明；
     - **简短摘要 (Short Description)**：一句话点明核心用途（<= 270 字符）；
     - **产品功能 (Product Features)**：3~5 条精炼卖点；
  4. **关键词（Tag）7个上限与残留清理自愈算法**：
     - 必须先自动展开「其他信息」折叠面板；
     - 读取当前页面已存在的关键词 Chip 数量与内容；
     - 若已有历史残留或默认标签超出 7 个，必须先定位每个非目标 Chip 上的删除按钮并点击移除；
     - 仅向 `#search-terms` 中追加缺失的目标词，**严格确保页面总关键词数量 $\le 7$ 个**，杜绝触发表单提交报错。
  5. **视觉资产上传与事件派发铁律**：
     - **桌面屏幕截图（必须至少 1 张 1920x1080 PNG）**；
     - **应用图标徽标**：1:1 酷图 (1080x1080)、300x300、150x150、71x71 PNG；
     - **事件派发**：CDP 的 `DOM.setFileInputFiles` 仅在 DOM 节点挂载 FileList，**必须紧接着派发 `input` 与 `change` 事件**，否则 Angular 上传处理器不会向 Azure Blob 发送上传请求；
     - **保存前硬核校验**：在点击保存前，**必须校验页面上 `桌面 (1)` 或缩略图已成功渲染**，未上传成功绝对禁止点击保存！
  6. **保存**：点击底部的 **「保存」** 按钮。

---

### 表单 6：提交选项 (Submission Options)
- **页面 URL**：`.../submissions/<submission-id>/options`
- **分阶段操作标准**：
  1. **阶段 1：现场 DOM 诊断查看 (Inspect)**：
     - 进入 options 页面，等待受限功能 API 后台请求返回并渲染 `<section>受限的功能</section>`；
     - 提取发布模式单选框当前选中项与 `runFullTrust` 理由输入框状态，向用户汇报，**停下来等待确认**。
  2. **阶段 2：填报与保存 (Fill & Save)**：
     - **发布暂缓选项 (Publishing Mode)**：默认设为 **「除非我选择“立即发布”，否则不发布此提交」(Manual)**；
     - **受限的功能 (Restricted capabilities) 说明**（⚠️ **必填项**）：
       使用 `textarea.text-area-width` 或全局穿透定位器定位输入框，填入合规理由并派发 `input`/`change`：
       `这是一个 WinUI 3 桌面应用，需要以全信任桌面进程运行才能正常启动并提供本地通知、文件和系统集成功能。应用仅在用户本机运行，不访问或修改其他用户的数据。`
     - 点击底部的 **「保存」** 按钮；
     - 导航回 Overview 页面确认状态变为「完成」。

---

## 阶段四：最终提交与审核追踪

1. 返回提交概览页面，确认 6 大表单模块均已显示**绿色勾选标记**；
2. 点击右上角的 **「提交到应用商店 (Submit to the Store)」**；
3. **审核流程与周期**：
   - **自动化安全扫描与预处理**：约 10~30 分钟；
   - **人工审核与合规检查**：通常 **24 ~ 72 小时**；
   - **上架成功**：审核通过后状态变为 `In the Store`，全球 Windows 用户均可在 Microsoft Store 搜索并直接下载安装！

---

## 阶段五：如何删除产品（用户自主操作指引）

> ⚠️ **安全红线**：删除产品属于高风险且不可逆操作，**严禁 Agent 通过自动化脚本代用户执行删除**。若用户需要删除产品，Agent **必须清晰指引用户在网页端自行操作**。

### 用户手动删除产品标准步骤：
1. **前置条件（清理提交草稿）**：
   - 若当前产品存在正在草拟的提交（Draft Submission），必须先在产品页面点击 **「删除提交」** 按钮，将提交草稿清理干净。
2. **进入产品概述页**：
   - 打开浏览器，访问该产品的控制台主页（例如 `https://partner.microsoft.com/zh-cn/dashboard/products/<ProductId>/overview`，即「应用程序概述」页面）。
3. **点击更多选项按钮（`...` 触发器）**：
   - 在产品标题右侧，找到并点击带有三个点的更多操作按钮（对应 DOM 中的 `<a slot="trigger" class="dropdown-toggle"><he-icon name="more"></he-icon></a>`）。
4. **点击「删除产品」**：
   - 在弹出的下拉菜单中，点击 **「删除产品」**（对应 DOM 中的 `<span class="delete-draft">删除产品</span>`）。
5. **在确认弹窗中二次确认**：
   - 阅读微软官方删除提示，确认无误后点击确定，即可将该产品及其预留名称从开发者中心彻底删除。
6. **刷新页面验证（⚠️ 关键注意点）**：
   - Partner Center 为单页 SPA 架构，删除完成后控制台列表可能存在前端缓存残留，**必须手动按 F5 刷新（或冷刷新）浏览器页面**，才能在「应用和游戏」产品列表中看到该产品已被彻底移除。


