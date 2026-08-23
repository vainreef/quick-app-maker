# Microsoft Partner Center 注册与操作指引（发布 Microsoft Store 必备）

本文档为 Agent 向用户提供微软开发者中心（Partner Center）从零注册、账号配置、包身份获取到商店资料提审的**全流程保姆级操作指引**。

---

## 模块定位与执行时机

- **触发时机**：当用户对本地安装的应用已经**充分把玩满意**，明确表示“想发布到微软商店 / 想分享给其他人”时触发。
- **职责划分**：
  - **用户操作**：注册账号、支付开发者年费、填写税务与收款表单、在网页端点击最终提交；
  - **Agent 协助**：提供清晰的操作步骤与截图指引、生成并核对 `Package.appxmanifest` 包身份、一键制作专属应用图标 Logo 与商店截图、编写中英文商品详情描述与亮点文案、生成符合商店规范的无签名/正式签名 MSIX 发布包。

---

## 一、 注册 Microsoft 开发者账号（用户操作，约 5~10 分钟）

### 1. 注册入口
- 访问 **[Microsoft Partner Center 注册官网](https://partner.microsoft.com/dashboard/registration/developer)**。
- 使用个人或公司的 Microsoft 微软账号（Outlook / Hotmail / 个人邮箱绑定的微软账号）登录。

### 2. 选择账号类型
| 账号类型 | 一次性注册费 | 适用场景 | 所需材料 |
| :--- | :--- | :--- | :--- |
| **个人账号 (Individual)** | 约 \$19 USD（一次性永久有效） | 独立开发者、个人作品、爱好者 | 个人身份信息、国际信用卡（Visa / MasterCard） |
| **公司账号 (Company)** | 约 \$99 USD（一次性永久有效） | 企业、工作室、商业团队 | 统一社会信用代码、企业邮箱、D-U-N-S 邓白氏编码、法人授权信息 |

> **提示**：中国大陆开发者使用 Visa / MasterCard 双币信用卡可直接完成在线支付。

### 3. 完善税务与付款资料（若应用免费则可稍后填写）
- 进入 **账户设置 (Account settings) → 财务明细 / 税务资料 (Payout and tax profiles)**；
- 个人开发者按页面提示完成电子版 W-8BEN 表单填写（声明非美国居民，享受税收协定优惠）。

---

## 二、 预留应用名称与获取正式包身份（核心关键步骤）

在打包正式商店应用前，必须先在 Partner Center 预留名称并获取微软分配的专属 **Package Identity**。

### 1. 预留应用名称
1. 登录 [Partner Center 控制台](https://partner.microsoft.com/dashboard/apps-and-games/overview)；
2. 点击 **「新建应用或游戏 (Create a new app or game)」**；
3. 选择 **「应用 (App)」**，输入你的应用名称（如 `旧时光` 或 `OldTimes`）；
4. 点击 **「检查可用性 (Check availability)」**，确认名称可用后点击 **「预留产品名称 (Reserve product name)」**。

### 2. 提取 3 大核心包身份参数
预留名称成功后，在应用管理左侧导航栏：
1. 点击 **产品管理 (Product management) → 产品标识 (Product Identity)**；
2. 页面会显示 3 项核心关键信息，将它们复制并提供给 Agent：

```xml
Package/Identity/Name:         12345YourName.AppName (例如: 61852Vainreef.OldTimes)
Package/Identity/Publisher:    CN=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (例如: CN=A1B2C3D4-E5F6-7890-ABCD-EF1234567890)
Package/Properties/PublisherDisplayName: Your Developer Name (例如: Vainreef Studio)
```

### 3. Agent 协助更新 `Package.appxmanifest`
Agent 将上述 3 项真实参数写入项目的 `Package.appxmanifest`：

```xml
<Identity
  Name="61852Vainreef.OldTimes"
  Publisher="CN=A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
  Version="1.0.0.0"
  ProcessorArchitecture="x64" />

<Properties>
  <DisplayName>旧时光</DisplayName>
  <PublisherDisplayName>Vainreef Studio</PublisherDisplayName>
  <Logo>Assets\StoreLogo.png</Logo>
</Properties>
```

---

## 三、 商店资料与物料准备（Agent 一键协助生成）

在 Partner Center 开始提审时，需要准备以下材料：

### 1. 视觉资产（Assets）清单
Agent 会在本地直接按尺寸渲染生成，放置于 `<app-slug>/Assets/`：
- `Square44x44Logo.png`（任务栏与开始菜单小图标）
- `Square150x150Logo.png`（磁贴与开始菜单中图标）
- `Wide310x150Logo.png`（宽磁贴）
- `StoreLogo.png`（50×50，商店详情页小标志）
- `SplashScreen.png`（启动欢迎屏）

### 2. 应用运行截图（至少 1 张，推荐 3~5 张）
- 尺寸必须为 **1920×1080** 或 **1366×768**；
- 展示核心功能卡片、添加修改交互、暗色/亮色风格。

### 3. 商店描述与文案
- **应用摘要 (Short description)**（100 字以内，如“温暖怀旧的纪念日回忆应用，每年提前一天提醒”）；
- **完整描述 (Description)**（排版优美，列出产品核心功能、设计理念、无内购与隐私说明）；
- **产品亮点 (App features)**（3~5 条短句）；
- **搜索关键字 (Search terms)**（如：`纪念日`, `倒计时`, `生日提醒`, `怀旧` 等）。

### 4. 年龄分级（IARC 国际年龄分级问卷）
- 在 Partner Center 中点击 **年龄分级 (Age ratings)**；
- 选择应用类别（实用工具 / 效率类）；
- 回答问卷（是否有暴力、色情、用户交流、位置共享等，通常全部选“否”）；
- 系统会自动秒级生成全球主要分级标准（ESRB, PEGI, IARC 全年龄认证）。

### 5. 隐私政策（Privacy Policy）
- 桌面应用涉及本地文件读写和系统通知时，需提供隐私政策链接；
- 示例声明：“本应用为纯本地离线应用，所有数据均存储在用户本机，不收集、不上传任何个人隐私数据”。

---

## 四、 构建与上传商店发布包（Store Package）

### 1. 执行 Publish 构建
```powershell
dotnet publish -c Release -r win-x64 -o ./publish
```

### 2. 制作 Store 上架包
```powershell
# 商店包使用 winapp 打包，无需本地自签名（上传后由微软商店官方证书签名）
winapp package ./publish --self-contained --executable <AppName>.exe -o ./store-package
```

### 3. 上传到 Partner Center
1. 进入 Partner Center 的 **提交页面 (Start your submission)**；
2. 进入 **程序包 (Packages)** 模块，拖拽上传生成的 `.msix` / `.msixupload` 文件；
3. 系统会自动验证程序包的 Identity 与 Capabilities（清理掉冗余的 `systemAIModels` 等未用权限）。

---

## 五、 提交审核与上线周期

1. 确认所有必填项（定价/市场：免费/全球、属性、年龄分级、描述截图、程序包）均显示绿色勾选；
2. 点击右上方 **「提交到应用商店 (Submit to the Store)」**；
3. **审核流程**：
   - **预处理与自动化安全扫描**（约 10~30 分钟）；
   - **人工审核与合规检查**（通常 24 ~ 72 小时）；
   - **发布上线**：审核通过后自动上架，全球用户均可在 Windows 微软应用商店直接搜索下载！
