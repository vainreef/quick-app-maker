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

## 阶段二：控制台建项与提取核心包身份

### 第 6 步：进入 Partner Center 控制台仪表板
- **入口网址**：`https://partner.microsoft.com/zh-cn/dashboard/home`
- 💡 **实测避坑点**：Partner Center 首页如果打开后一片空白（前端常见加载 Bug），点击左上角**三条线（汉堡菜单）→ 点击「应用和游戏」**即可正常加载控制台。

### 第 7 步：创建新产品（保留应用名称）
1. 在「应用和游戏 | 概述」页面，点击右上角 **「+ 新产品」** 下拉菜单；
2. 选择第 1 项：**「MSIX 或 PWA 应用」**（`data-automation-id="create-new-Application"`）；
3. 输入想要发布的应用名称（例如 `旧时光` 或 `OldTimes`），点击“检查可用性”；
4. 确认名称可用后，点击 **「保留产品名称 (Reserve product name)」**；
5. ⚠️ **规则提醒**：保留名称后应用处于草稿状态，**必须在 3 个月内提交至应用商店**，否则名称会自动失效释放。

### 第 8 步：提取产品标识（Product Identity）回填工程
进入该产品的「应用程序概述」页面：
1. 展开左侧导航菜单 **「产品管理」→「产品标识 (Product Identity)」**；
2. 页面会生成微软官方分配的专属核心凭据：
   ```xml
   Package/Identity/Name:         Vainreef.OldTimes (示例)
   Package/Identity/Publisher:    CN=311E7A53-AF43-454B-821E-5554DC1F27F5 (示例)
   Package/Properties/PublisherDisplayName: Vainreef (示例)
   ```
3. **Agent 核心动作**：Agent 将这 3 项参数准确回填至项目源码的 `Package.appxmanifest`，确保打包生成的 MSIX 拥有合法的商店官方身份。

---

## 阶段三：商店 6 大表单逐项填报指引（Agent 辅助填表核心）

在产品的「应用程序概述」页点击 **「开始提交 (Start Submission)」**，依次走完以下 6 个表单模块：

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
  5. **保存**：配置完成后，底部的 **「保存草稿」** 按钮激活生效，点击保存。

---

### 表单 2：属性 (Properties)
- **页面 URL**：`.../submissions/<submission-id>/properties`
- **Agent 推荐填报标准**：
  1. **类别和子类别 (Category & Subcategory)**：
     - 主类别：根据应用性质选择（如：`效率 Productivity` 或 `实用工具 Utilities`）；
     - 子类别：选择对应的细分项；
  2. **隐私策略 (Privacy Policy)**：
     - 第一个下拉框选择 **「否，我的产品不使用任何个人信息」**；
     - 下方选择 **「提供隐私策略文本」**，直接填写应用自己的隐私策略文字；
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
  1. **调查表模式**：选择 **「我已准备好填写 IARC 调查表」**；
  2. **应用类型**：选择 **「其他所有应用类型 (All Other Application Types，value=2558)」**（切勿选游戏或社交通信类，避免冗余问卷）；
  3. **物理介质分发**：选择 **「否 (No)」**；
  4. **后续敏感内容问卷**（暴力、粗俗言语、色情、恐怖、毒品、位置共享、用户交流等）：**全部选择「否」**；
  5. **保存**：系统自动计算并展示全球各大分级机构评定（ESRB Everyone, PEGI 3, IARC 3+ 全年龄认证），点击 **「保存草稿」**。

---

### 表单 4：程序包 (Packages)
- **页面 URL**：`.../submissions/<submission-id>/packages`
- **Agent 推荐填报标准**：
  1. **构建商店包**：Agent 执行本地打包命令，生成合规的 `.msix` 发布包：
     ```powershell
     dotnet publish -c Release -r win-x64 -o ./publish
     New-Item -ItemType Directory -Force ./store-package | Out-Null
     winapp package ./publish --self-contained --executable <AppName>.exe --output ./store-package/<Identity>_<Version>_x64.msix
     ```
  2. **上传程序包**：在 `#pkg_upload` 区域拖拽或选择生成的 `.msix` 文件；
  3. **设备系列可用性 (Device family availability)**：
     - 必须勾选：**「Windows 10/11 Desktop」**；
     - 勾选：**「让 Microsoft 决定是否向未来的设备系列提供此应用」**；
  4. **保存**：上传并校验通过后，点击底部的 **「Save」** 按钮。

---

### 表单 5：Store 一览 (Store Listing & 语言物料)
- **页面 URL**：`.../submissions/<submission-id>/listings`
- **Agent 推荐填报标准**：
  1. **语言管理**：系统自动根据上传的包识别语言（默认中文 `zh-CN`，可追加 `en-US`）；
  2. **文案物料（Agent 提前为用户拟定并润色）**：
     - **应用标题**：与预留名称一致；
     - **简短摘要 (Short Description)**：100 字以内，点明核心用途；
     - **完整描述 (Description)**：排版工整，涵盖“产品亮点”、“使用场景”、“设计理念”与“离线安全保证”；
     - **产品功能 (App Features)**：3~5 条精炼卖点；
     - **搜索关键字 (Search Terms)**：3~5 个高频中文词汇；
  3. **视觉物料（Agent 提前在本地生成合格尺寸）**：
     - **应用截图 (Screenshots)**：上传 1~5 张 1920×1080 真实无拉伸运行截图；
     - **应用图标 (App Logo)**：上传 1:1 专属高分辨率 Logo（300×300 或 150×150 PNG）；
  4. **保存**：点击底部的 **「保存」** 按钮。

---

### 表单 6：提交选项 (Submission Options)
- **页面 URL**：`.../submissions/<submission-id>/options`
- **Agent 推荐填报标准**：
  1. **发布暂缓选项 (Publishing Mode)**：
     - 默认选择 **「除非我选择“立即发布”，否则不发布此提交」(Manual)**，认证通过后保留人工发布控制；
     - 只有用户明确要求自动发布时才选择 **「尽快 (Asap)」**。
  2. **认证说明 (Notes for Certification)**：当前页面主要显示跳转提示，详细测试信息按页面入口填写；
  3. **受限的功能 (Restricted capabilities)**：如果包声明 `runFullTrust`，填写页面出现的用途说明框（最多 500 字），例如：
     `这是一个 WinUI 3 桌面应用，需要以全信任桌面进程运行才能正常启动并提供本地通知、文件和系统集成功能。应用仅在用户本机运行，不访问或修改其他用户的数据。`
  4. **保存**：点击底部的 **「保存」** 按钮。

---

## 阶段四：最终提交与审核追踪

1. 返回提交概览页面，确认 6 大表单模块均已显示**绿色勾选标记**；
2. 点击右上角的 **「提交到应用商店 (Submit to the Store)」**；
3. **审核流程与周期**：
   - **自动化安全扫描与预处理**：约 10~30 分钟；
   - **人工审核与合规检查**：通常 **24 ~ 72 小时**；
   - **上架成功**：审核通过后状态变为 `In the Store`，全球 Windows 用户均可在 Microsoft Store 搜索并直接下载安装！
