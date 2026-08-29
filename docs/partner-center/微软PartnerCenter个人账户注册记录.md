# Microsoft Partner Center 个人账户注册过程记录

> 记录日期：2026-08-23
> 目标：注册微软 Partner Center 个人开发者账户（Microsoft Store 开发人员）

---

## 第 1 步：创建微软账号

- 网址：<https://signup.live.com/signup>
- 操作：使用自己的 Gmail 邮箱直接绑定注册（未创建 @outlook.com 邮箱，Gmail 即为登录邮箱）
- 结果：**创建成功**

### 补充说明（遇到的问题）

- 在网页 <https://signup.microsoft.com/> 注册时，一直提示"真人验证码异常"，无法通过人机验证
- 解决办法：改用**本机的 Xbox 应用**内注册，成功完成账号创建

---

## 第 2 步：打开 Microsoft Store 开发人员入口

- 网址：<https://storedeveloper.microsoft.com/>
- 操作：登录后进入 onboarding 页面开启 Partner 开发者账户
- 登录后跳转到：<https://storedeveloper.microsoft.com/zh-Hans/onboarding>

---

## 第 3 步：Onboarding 流程 — 选择账户类型

页面标题：成为 Microsoft Store 开发人员

页面显示的流程步骤：

1. 帐户类型 —— 选择你的帐户类型
2. 业务详细信息 —— 输入企业信息
3. 联系人详细信息 —— 提供联系人详细信息
4. 帐户验证 —— 验证你的企业和雇佣关系

（注：以上为公司帐户的完整流程；选择"个人开发者"后流程变为下面的版本）

两个选项：

| 选项 | 说明 | 费用 |
|------|------|------|
| **个人开发者** ✅ | 适用于以个人名义发布的业余爱好者、学生和独立开发者 | 免费 |
| 公司帐户 | 适用于以公司或组织名义发布的企业、自由职业者和团队；在"联系人详细信息"步骤中需要使用与组织的域关联的工作电子邮件进行验证 | 免费 |

- 页面提示：需要使用 Microsoft 个人帐户或与组织关联的工作帐户登录才能继续操作。如果没有，则可在下一步中创建一个。
- **选择：个人开发者**

---

## 第 4 步：身份验证

选择"个人开发者"后，流程步骤变为：

1. 帐户类型 —— 选择你的帐户类型 ✅（已完成）
2. 身份验证 —— 验证你的身份
3. 个人资料详细信息 —— 完成个人资料
4. 帐户设置 —— 创建开发人员帐户

页面内容：

- 标题：身份验证 —— 验证你的身份以确保帐户安全性和合规性
- 页面上显示了注册账号的姓名：**罗运来**（页面显示为"运罗"，疑为显示顺序问题）
- 要求的验证方式：**上传身份证件**（身份证件照片上传验证）
- 按钮：返回 / 继续
- 结果：上传证件并点击继续，**验证通过**

---

## 第 5 步：个人资料详细信息 + 帐户设置

- 操作：一路填表完成"个人资料详细信息"和"帐户设置"
- 发布者名称（显示在 Microsoft Store 商店页面上的名字）：**vainreef**
- 结果：**开发人员帐户创建成功**

---

## 第 6 步：进入 Partner Center 仪表板 ✅ 注册全部完成

- 网址：<https://partner.microsoft.com/zh-cn/dashboard/home>
- 问题：**首页每次打开都一片空白**（稳定复现，非偶发）
- 解决办法：点击左侧的"三条线"（汉堡菜单）→ 点击"应用和游戏"，即可正常显示内容

### 应用和游戏 | 概述 页面结构

左侧导航菜单：

- 主页
- 应用和游戏
  - 概述
  - 吸引
  - 促销代码
  - 参与
  - 客户组
  - 定向优惠
- Xbox 服务
- Web 服务
- 沙盒
- Xbox 测试帐户
- 信赖方
- 业务合作伙伴
- Xbox 开发主机

页面顶部提示：

> Game publishing options have been updated. Choose GDK Game for GDK-based titles, or MSIX or PWA game for UWP and PWA games.
> （游戏发布选项已更新：GDK 游戏用于基于 GDK 的游戏，MSIX 或 PWA 游戏用于 UWP 和 PWA 游戏）

页面主要内容：

- "查看分析报告"按钮
- "新产品"按钮 —— **发布应用和游戏的入口**
- 显示/隐藏产品、筛选器
- 产品列表表头：名称 / 类型 / 已包含 / 市场 / 基本价格 / 状态
- 当前提示："立即开始发布应用和游戏。选择'新产品'以开始使用"
- **当前产品数量：0 个**

---

---

## 第 7 步：创建新产品 — 保留应用名称

操作路径：概述页 →"新产品"下拉 → **MSIX 或 PWA 应用**

弹出对话框：**保留产品名称**

> 通过保留某个名称来创建应用
> 在你保留某个名称后，我们将在你的应用中预配推送通知等服务，你也可以开始定义加载项。
>
> ⚠️ 确保你有权使用保留的任何名称。**必须在三个月内将此应用提交到 Microsoft Store，否则将会丢失保留的名称。**
>
> 名称输入框 + "检查可用性" + 按钮：保留产品名称 / 取消

- 填写的名称：`TestProjectABCDEFG99871`
- 结果：**保留成功，产品已创建**

### 概述页在"有产品"时的表格行结构（已更新至快照文件）

创建产品后，概述页表格出现一行数据（`tbody tr.cdk-row`），单元格结构：

| 列 | 内容 | 链接 |
|----|------|------|
| 名称 | `TestProjectABCDEFG99871`（product-link > a） | `/zh-cn/dashboard/products/9P91R044KH9G/overview` |
| 类型 | `MSIX 或 PWA 应用`（title 属性同） | - |
| 已包含 | `0 个加载项`（data-l10n-key=Included_Addons_Count） | `/zh-cn/dashboard/products/9P91R044KH9G/addons` |
| 市场 | `242` | `/zh-cn/dashboard/products/9P91R044KH9G/submissions/1152921505701720046/availability` |
| 基本价格 | `--`（p[title="--"]） | - |
| 状态 | `未提交`（data-l10n-key=Submission_Status_InProgress） | - |
| （折叠列） | 空 | - |

- 结果计数变为："正在显示 1 个结果。"
- 空状态提示（"立即开始发布应用和游戏…"）消失

---

## 第 8 步：应用程序概述页（新产品已创建 ✅）

页面标题（h1.title）：TestProjectABCDEFG99871
副标题：MSIX 或 PWA 应用
状态标签（status-yellow）：**处于草稿状态**
右上角：删除产品的下拉列表（he-dropdown → "删除产品"）；联系客户支持链接 <https://aka.ms/storedevsupport>

### 产品左侧导航菜单结构

- 应用程序概述（当前选中）
- 加载项
- 产品页面试验
  - 试验详细信息
- 产品管理
  - 产品标识
  - 管理应用名称
  - 管理程序包
  - WNS/MPNS
- 服务
  - Xbox 服务
  - Xbox 预览体验计划
  - 地图
  - 产品收集和购买
  - 管理员同意
- 补充信息
  - 其他测试信息

### 页面主要内容

1. **产品版本** 区域："提交产品"卡片，含文档链接和 **"开始提交"按钮**（he-button appearance="primary"，data-l10n-key=Start_Submission）
2. 加载项 / 捆绑包区域（app-add-ons、app-bundle，暂无内容）
3. **常规** 区域 →"查看产品标识"折叠卡片

### 产品标识信息（重要）

| 标识项 | 值 |
|--------|-----|
| Package/Identity/Name | `Vainreef.TestProjectABCDEFG99871` |
| Package/Identity/Publisher | `CN=311E7A53-AF43-454B-821E-5554DC1F27F5` |
| Package/Properties/PublisherDisplayName | `Vainreef` |
| Package Family Name (PFN) | `Vainreef.TestProjectABCDEFG99871_tbcnmw1yjv394` |
| Package SID | `S-1-15-2-1608095143-2846274818-3670008961-2812161334-2238714474-3970647913-3819985392` |
| Store ID | `9P91R044KH9G` |
| Microsoft Store 深层链接 | 产品上线后可用 |
| Web Store URL | 产品上线后可用 |

### 关键自动化选择器

- 提交入口按钮：`he-button[data-l10n-key="Start_Submission"]`
- 产品标识展开开关：`a[aria-controls="collapseApplicationIdentity"]`
- 删除产品菜单：`he-dropdown[aria-label="删除产品的下拉列表"]` → `span.delete-draft`

---

## 第 9 步：提交流程 — 定价和可用性（第 1 个表单页）

页面标题：`定价和可用性 | TestProjectABCDEFG99871 | 合作伙伴中心`
页面 URL（推断）：`/zh-cn/dashboard/products/9P91R044KH9G/submissions/1152921505701720046/availability`
（提交 ID：`1152921505701720046`，见"查看转换表"链接）

### 当前页面状态

- 页面级错误（2 条）："没有为可购买的产品创建 PriceSchedule。"、"必须为此产品配置价格。"
- 底部"保存草稿"按钮（`#saveButtonPricing` / `uitestid="saveButtonPricing"`）：**当前 disabled**
- 页面为混合技术栈：AngularJS（ng-view 传统表单）+ Angular 11（ng-version="11.2.14" 新组件）

### 页面分区结构

| 分区 | 标题 | 状态 |
|------|------|------|
| sales-markets | 市场 | 可见（全球所有市场[推荐] / 限定特定市场 单选）|
| visibility | 可见 | 选项已展开；受众=开放受众；可发现性=可用且可发现 |
| visibilityv2 | 可见 v2（加载项专用） | 隐藏（ng-hide） |
| dates | 计划 | 已展开；基准计划 发布=尽快、停止购置=永不 |
| display | 显示发布日期 | 隐藏 |
| market-groups | **定价**（Angular 组件） | 可见；Default 市场组 = **240 个市场**；基本价格未配置（he-select disabled） |
| trial | 免费试用 | 未配置（无免费试用） |
| deals | 销售定价 | 没有配置销售定价 |
| entitlements | 随意使用 | 隐藏 |
| licensing | 组织许可 | 存在（企业关联），未配置 |
| revenue | 收入 SKU 信息 | 隐藏 |

### 关键元素与测试选择器

- 保存：`input#saveButtonPricing[value="保存草稿"]`
- 发布：`a[href="/zh-cn/dashboard/products/9P91R044KH9G/submit"]`（仅 Jaguar 显示，当前隐藏）
- 市场组搜索：`he-search-box[uitestid="marketGroupSearchBox"]`
- 创建市场组：`he-button:contains("创建新市场组")`（两处：命令栏 + 列表底部）
- 计划：发布 `select[uitestid="AvailableSelector-0"]`（尽快/在）；停止购置 `select[uitestid="StopSellingSelector-0"]`（永不/在）
- 特定受众链接（受限版商店页）：`https://apps.microsoft.com/detail/restricted/9P91R044KH9G`
- 免费试用类型：`select[aria-label="TrialType"]`（无免费试用/时间限制/无限制）
- 4 个分支管理弹窗：import/create/rename/delete-branch-popup

---

## 第 10 步：属性页（第 2 个表单页）

页面标题：`TestProjectABCDEFG99871 | 属性`

### 重要变化：左侧导航出现"提交 1"分支

提交 1（本次提交）下包含 6 个步骤（当前在 **属性**）：

1. 定价和可用性
2. **属性**（当前选中）
3. 年龄分级
4. 程序包
5. Store 一览
6. 提交选项

### 属性页分区结构

| 分区 | 标题 | 内容/状态 |
|------|------|-----------|
| 1 | 类别和子类别* | `select[name="CategorySelect"]`（25 类）；子类别 `select[name="SubcategorySelect"]`（当前 disabled）；次要类别 `select[name="SecondaryCategorySelect"]` |
| 2 | 隐私策略 | `select[name="privacyPolicySelection"]`（选择一个答案…/是/否）；Support info (optional)：网站、支持部门联系信息、电话号码、地址行 1/2、邮政编码、市/县、省/直辖市、国家/地区 + Preview 按钮 |
| 3 | 显示模式 | WMR：电脑/ HoloLens 复选框；边界设置单选（坐姿+站姿 / 所有体验，当前 disabled） |
| 4 | 产品声明 | 7 个复选框：store、accessibility、storage（**已勾选**）、backups（**已勾选**）、windows（**已勾选**）、penInk、usesGenAI；附注：仅"游戏"类别支持广播和录制 |
| 5 | 系统要求 | 硬件表：12 个功能复选框（触摸屏…WMR 头显）+ 内存/ DirectX / 视频内存下拉 + 处理器/图形文本框 |

### 关键元素选择器

- 保存按钮：`button[name="save_button"]`（btn btn-primary，可用状态）
- 类别：`select[name="CategorySelect"]`、`select[name="SecondaryCategorySelect"]`
- 隐私策略：`select[name="privacyPolicySelection"]`
- 内存：`select[name="RamMinSelect"]` / `select[name="RamRecSelect"]`（未指定/300MB…20GB）
- DirectX：`select[name="directxMinSelect"]` / `select[name="directxRecSelect"]`（9/10/11/DX12 FL11/DX12 FL12）
- 视频内存：`select[name="VideoRamMinSelect"]` / `select[name="VideoRamRecSelect"]`（1-6GB）
- 产品声明复选框：`he-checkbox[name="'storage-checkbox'"]` 等（7 个：store/accessibility/storage/backups/windows/penInk/usesGenAI）

---

## 第 11 步：年龄分级页（第 3 个表单页）

页面标题：**年龄分级**（无左侧菜单框架，独立居中布局 `#ageratingsweb`）

### 页面结构

1. 顶部说明：须准确回答以下问题并接收 IARC 年龄分级；已有 IARC 证书 ID / GRID 则在此输入
2. 模式选择（radio `name="inputMode"`）：
   - `questionnaire`（默认）"我已准备好填写 International Age Rating Coalition (IARC)调查表。"
   - `import` "我已在其他地方填写了此应用的调查表，并具有 IARC 证书 ID 或全球分级 ID。"
3. 导入分级区块（`hidden`）：`he-text-field` + "搜索"按钮（`importForm`）
4. **分级调查表**区块（可见）：
   - 问题 1：应用类型（`data-questionid="1109"`，id="question#1109"）：
     - 游戏 `value="1827"` / 社交或通信 `value="2555"` / 其他所有应用类型 `value="2558"`（radio name="question#1109"）
   - 后续问题容器 3 个（`followup-questions`，空）— 选择后会动态加载
5. 物理分发问题（radio-question，英文）："Will this product use ratings obtained directly from a ratings board, and/or will it be distributed on physical media in any region?" → 是 `#yesVal` / 否 `#noVal`
6. 底部按钮：**保存草稿**（he-button primary）+ **取消**（secondary）

### 关键选择器

- 模式：`input[name="inputMode"][value="questionnaire"]` / `[value="import"]`
- 应用类型：`input[name="question#1109"]`（1827/2555/2558）
- 物理分发：`#yesVal` / `#noVal`
- 保存：`he-button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"]`

---

## 第 12 步：程序包页（第 4 个表单页）

页面标题：`TestProjectABCDEFG99871 | Packages`（页面文字为英文）

### 页面结构

1. 顶部提示条（he-message-bar）：Arm32 弃用通知（建议转 Arm64）
2. 说明：使用 Visual Studio 时需用与开发者账户相同的账号登录
3. **程序包上传区**（`#pkg_upload` / lib-uploader）：
   - `<input type="file" name="fileuploader" class="fileuploader" multiple accept="">`
   - 支持格式：`.msix, .msixbundle, .msixupload, .appx, .appxbundle, .appxupload, .xap`（拖放或浏览文件）
4. Device family availability（设备系列可用性）：
   - 警告（当前无包）："This product will not be available to customers on Windows 10/11 unless you check one or more device family boxes."
   - 5 个设备系列复选框（均未勾选）：Windows 10/11 Desktop / Windows 10 Mobile / Windows 10/11 Xbox / Windows 10 Team / Windows 10 Mixed Reality
   - "Let Microsoft decide whether to make this app available to any future device families"（**已勾选**）
5. 空组件区：app-packages-platform / app-package-rollout / app-package-managebyrelease / app-mandatory-available-date（上传包后才会显示内容）
6. 底部：**Save** 按钮（`input[type="button"].btn-primary[value="Save"]`）

### 关键选择器

- 上传：`input[type="file"][name="fileuploader"]`（多文件）
- 保存：`input[value="Save"]`
- 设备系列复选框：`he-checkbox:has(.text-body)`（Desktop/Mobile/Xbox/Team/Mixed Reality + future）

---

## 第 13 步：Store 一览页 — 管理 Store 一览语言（第 5 个表单页）

页面标题：**管理 Store 一览语言**（submission-listing-summary 组件）

### 页面结构

1. 标题 + 副标题："Select the languages you want to support. Then, click each selected language to enter and manage its Store listing details."
2. **软件包中支持的语言**（`#manage-package-languages`）：
   - he-data-grid（language-data-grid），空记录提示："上传程序包后，我们将在此处显示语言。"
   - 搜索框（`lib-search[name="packages-languages-search-bar"]`）
3. **其他 Store 一览语言**（`#manage-additional-languages`）：
   - "管理其他语言"按钮（secondary）→ 打开 wide modal（语言选择弹窗）
   - ⚠️ 提示："其他语言的 Store 一览使用此提交软件包中的一个图标。"
   - 搜索框（`lib-search[name="additional-languages-search-bar"]`）
4. 底部：**保存**（primary）/ **取消**（secondary）

### 关键选择器

- 管理其他语言：`he-button:has([data-l10n-key="appsubmission_manage_languages_modallink"])`
- 保存：`he-button[data-l10n-key="appsubmission_savebutton"]`（实际为内部 span，按钮本身无 key，需按文本"保存"定位）
- 语言列表：`he-data-grid.language-data-grid`
- 语言管理弹窗：`lib-modal > .modal-dialog.wide`

---

## 第 14 步：提交选项页（第 6 个表单页 — 提交流程最后一步）

页面标题：`TestProjectABCDEFG99871 | 提交选项`

### 页面结构

1. **发布暂缓选项**（`data-bi-area="PublishDate-Section"`）— radio `name="PublishMode"`：
   - `Asap`（`#radioReleaseDate_asap`，默认）："在此提交通过认证后立即予以发布(或者根据你在"计划"部分选择的日期予以发布)"
   - `Manual`（`#radioReleaseDate_manual`）："除非我选择"立即发布"，否则不发布此提交"
   - `SpecificDate`（`#radioReleaseDate_specifc`）："开始发布此提交" + 日期输入（`#DateInput`，当前 disabled）+ 时间下拉（24 小时制，当前选 上午11:00，UTC）
   - 两个隐藏警告：`#emptypublishdateerror`（选择日期或留空尽快发布）、`#pastPublishDateWarning`（过去日期将立即发布）
2. **认证说明**：跳转"其他测试信息"页（`products/9P91R044KH9G/suppinfo/additionaltestinginfo`）；客户看不到此信息
3. **提交通知受众**：链接"点击此处"→ `/zh-cn/dashboard/products/9P91R044KH9G/notifications`
4. 底部：**保存**（`button[data-l10n-key="optionsSave"]`，btn btn-primary）

### 关键选择器

- 发布模式：`input[name="PublishMode"]`（value: Asap / Manual / SpecificDate）
- 发布日期：`input#DateInput`
- 时间下拉：`.time-picker .dropdown-menu button[role="menuitem"]`（value 0-23）
- 通知受众：`a[href="/zh-cn/dashboard/products/9P91R044KH9G/notifications"]`

## 待办 / 下一步

- [x] 在身份验证页面上传身份证件，点击"继续"
- [x] 完成"个人资料详细信息"（填写个人资料）
- [x] 完成"帐户设置"（创建开发人员帐户）
- [x] 通过"新产品"→ MSIX 或 PWA 应用创建产品 `TestProjectABCDEFG99871`（Store ID: 9P91R044KH9G，处于草稿状态）
- [ ] ⚠️ 三个月内完成提交（2026-11-23 前），否则丢失保留名称
- [ ] 点击"开始提交"，走产品提交流程
- [ ] 定价和可用性：配置价格（当前报错：未创建 PriceSchedule、必须配置价格），保存草稿按钮当前禁用
- [ ] 属性：填写类别/隐私策略等，点"保存"（按钮可用）
- [ ] 年龄分级：填 IARC 调查表（应用类型等），保存草稿
- [ ] 程序包：上传 .msix/.appx 等包文件，勾选设备系列，Save
- [ ] Store 一览：管理语言（上传包后才有包内语言；可"管理其他语言"），保存
- [ ] 提交选项：选择发布模式（默认 Asap 立即发布），保存

## 页面 HTML 快照

- "应用和游戏 | 概述"页面的真实 DOM 结构已保存至：`页面快照-应用和游戏概述.html`（已更新为"有产品"状态：含 1 行产品数据）
- "应用程序概述"（产品 TestProjectABCDEFG99871）页面的真实 DOM 结构已保存至：`页面快照-应用程序概述.html`
- "定价和可用性"提交页面的真实 DOM 结构已保存至：`页面快照-定价和可用性.html`（注：页面开头的 he-theme CSS 变量 `<style>` 块已省略，非页面结构）
- "属性"提交页面的真实 DOM 结构已保存至：`页面快照-属性.html`
- "年龄分级"提交页面的真实 DOM 结构已保存至：`页面快照-年龄分级.html`
- "程序包"提交页面的真实 DOM 结构已保存至：`页面快照-程序包.html`
- "Store 一览（管理语言）"提交页面的真实 DOM 结构已保存至：`页面快照-Store一览-管理语言.html`
- "提交选项"提交页面的真实 DOM 结构已保存至：`页面快照-提交选项.html`
- 快照说明：保留了真实标签结构、class、id、role、aria-*、data-automation-id、data-l10n-key 等有效属性；仅移除了 Angular 框架噪音（elementtiming、_ngcontent-\*、_nghost-\*、ng-star-inserted、ng-tns-\*、空注释节点）

### "新产品"下拉菜单选项速查（自动化关键信息）

| 菜单文字 | button value | data-automation-id |
|----------|--------------|--------------------|
| MSIX 或 PWA 应用 | `MSIX_PWA_App` | create-new-Application |
| EXE 或 MSI 应用 | `EXE_MSI_App` | create-new-win32-application |
| GDK game | `GDK_Game` | create-new-Application |
| MSIX 或 PWA 游戏 | `MSIX_PWA_Game` | create-new-Application |

- 下拉触发按钮：`button[data-automation-id="create-new-button"]`

## 已确认信息

1. ✅ Gmail 是直接绑定注册的登录邮箱（未生成 outlook.com 地址）
2. ✅ 页面上的"运罗"是姓名显示（本人姓名：罗运来）
3. ✅ 身份验证步骤要求上传身份证件
4. ✅ 发布者显示名称：vainreef
5. ⚠️ Partner Center 首页（dashboard/home）每次打开都空白，需经左侧汉堡菜单 →"应用和游戏"进入
