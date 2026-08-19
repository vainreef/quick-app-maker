# Fast Publish Capability Boundary

## Definition

Fast Publish Mode 的边界由基础设施、数据流、权限、商业化和 Store 前提组成。产品类别本身不是分类器：聊天、图片、天气、文件、学习、记账和游戏都可以进入 Fast Mode，只要实现方式落在契约盒子里。

> **免费、本地、个人、通用；固定技术栈；Microsoft Store only。**

目标是保留用户的核心价值，把实现投影到这个盒子里；需要改变盒子时，记录 Advanced Mode candidate，并给出一条可交付的 Fast Mode projection。

## Capability matrix

| 维度 | Fast Mode invariant | Fast Mode 形式 | Advanced Mode trigger |
| --- | --- | --- | --- |
| 平台 | Windows 本地桌面应用 | Windows 10 1809+ / Windows 11，x64 V1 | macOS、Linux、Android、iOS、Web 作为同一交付目标 |
| 分发 | Microsoft Store + MSIX | Store 安装、更新、发现与分发 | EXE/MSI 直链、独立更新器、站点下载链路 |
| 签名 | Store MSIX 由 Store 流程处理 | 本地测试使用开发证书；提交后由 Store 处理生产签名 | Store 外部发布、自己维护 CA 证书和签名基础设施 |
| 后端 | 自建服务器边界为空 | 本地计算；第三方公开 API 通过 HTTPS 访问 | 自建 Backend、数据库服务器、License Server、任务队列 |
| 域名 | 应用逻辑独立于网站 | Store listing 与按需的隐私政策 URL | 回调域名、登录门户、远程管理后台成为核心依赖 |
| 数据 | 本地数据优先 | JSON、SQLite、用户选择的文件夹和本机缓存 | 用户账号、云同步、远程备份、跨设备状态 |
| 开发者秘密 | 客户端不承载项目方秘密 | 用户在运行时填写自己的 API key；密钥只进入本机受控存储 | 把项目方 API key、统一额度或统一计费放进客户端 |
| 网络 | `HttpClient` 受控接入 | 稳定公开 API、用户选择的 endpoint、无隐藏 secret 的服务 | 需要隐藏凭据、统一计费、统一额度或服务端业务规则 |
| 商业化 | 免费 Offer | Store Base price = Free | 收费、订阅、IAP、广告、License、收入结算 |
| 开发者身份 | 普通个人开发者 | Microsoft Account + Partner Center 个人流程 | 公司主体、DUNS、组织审批、企业身份体系 |
| 权限 | 普通桌面权限 | 窗口、文件选择器、本地存储、用户选择的媒体/文本 | 管理员、驱动、内核、系统服务、全局键盘拦截、特殊系统数据 |
| Manifest | 最小 Capability 集 | 按实际功能声明普通能力 | Restricted Capability、elevation、需要额外 Store 审批的能力 |
| 合规 | General Utility | 效率、创作、学习、生活、本地工具、个人数据处理 | 金融/医疗/赌博运营、特殊行业资质、组织资质、未解决的版权链路 |
| 隐私 | 本地优先 | 遥测、广告追踪、用户画像、内容上传默认关闭 | 账号体系、用户数据收集/传输、远程画像、广告网络 |

## Network is not the same as a backend

把网络请求按数据流判断，而不是按“联网”两个字判断。

### Fast Mode: public API or user-owned credential

```text
Windows App → HTTPS → public weather / exchange-rate / catalog API
```

适用场景：

- 用户输入城市，客户端查询公开天气 API。
- 用户输入币种，客户端请求公开汇率 API。
- 用户在 App 设置页填写自己的 AI API key，客户端直接访问对应 endpoint。
- API 不需要项目方隐藏 secret，或 secret 由最终用户自行承担。

实现要求：

- 使用 `HttpClient`。
- 将 endpoint、超时、错误状态和离线状态做成可见的应用状态。
- 用户密钥进入本机受控存储；密钥不进入源代码、manifest、日志、截图、Build 归档或提交包。
- 为公开 API 设置合理的重试与速率限制提示。

### Advanced Mode: project-owned service boundary

```text
Windows App → project-owned Backend → provider API
```

触发项：

- 所有用户共享项目方充值的 AI key。
- 项目方隐藏 provider key、统一控制额度或按用户计费。
- 服务端保存账号、云数据、任务队列、权限规则或审计记录。

Fast Mode projection：把 AI App 降为“用户自行填写 key 的本地客户端”；把云照片工具降为“本地照片管理器”；把共享账户工具降为“本机单用户数据”。

## Product projections

### Weather app

| User goal | Classification | Fast Mode projection |
| --- | --- | --- |
| 输入城市查看天气 | Direct | 本地设置 + 公开天气 API |
| 自动定位城市 | Controlled addition | 用户授权定位 + 隐私/Capability 检查 |
| 账号、收藏、跨设备同步、推送预警 | Advanced candidate | 本地收藏与手动刷新版本 |

### AI chat app

| User goal | Classification | Fast Mode projection |
| --- | --- | --- |
| 用户填写自己的 API key | Direct | 本地对话记录 + 用户自有 endpoint |
| 所有用户使用项目方充值 key | Advanced candidate | 用户自行填写 key 的客户端 |
| 账号、额度、云历史、团队共享 | Advanced candidate | 本地单用户模式 |

### Photo/file utility

| User goal | Classification | Fast Mode projection |
| --- | --- | --- |
| 本地整理、重命名、拼图、导出 | Direct | File picker + 本地处理 + 本地输出 |
| 自动访问用户指定文件夹 | Controlled addition | 由用户选择目录并记录 FutureAccessList |
| 云同步、团队共享、后台上传 | Advanced candidate | 本地文件工作流 |
| 读取 NTFS MFT、管理员扫描整个磁盘 | Advanced candidate | 普通文件选择器扫描版本 |

### Personal ledger

| User goal | Classification | Fast Mode projection |
| --- | --- | --- |
| 本地记录收支与统计 | Direct | JSON/SQLite 本地账本 |
| 连接银行、自动扣款、投资交易 | Advanced candidate | 手动导入 CSV 的本地分析器 |

## Request triage protocol

1. **Phrase the idea**：用一句话写出用户真正想做的产品和最重要的体验。
2. **Inspect the boundary**：扫描 server、account、sync、secret、pricing、permission、regulated-data 和 personal-data signals。
3. **Choose a path**：
   - `direct`：全部落在 Fast Mode，完成需求确认后实现。
   - `projected`：核心价值可用本地/公开 API/用户凭据版本交付，先说明投影再实现。
   - `advanced-candidate`：完整需求需要改变基础设施或合规前提，保留 Fast Mode projection，并把完整版本标记为 Advanced Mode。
4. **Keep the user goal visible**：说明保留的体验、替换的基础设施和新版本的限制。
5. **Ask at the correct checkpoint**：当投影会改变核心交互或数据所有权时，请用户确认；纯工具链、编译、检查和归档动作由 Agent 继续完成。

## User-facing boundary language

这一阶段发生在产品确认之后。内部分析可以使用技术词，面向用户时改用体验和生活场景表达：

```text
你想要的重点是：[核心体验]。
其中 [某项体验] 会让应用长期依赖另一套在线服务、统一身份、收费流程或更深层的电脑访问。
第一版我建议这样处理：[用户能直接理解的修改版本]。
这样会保留：[保留内容]；暂时调整：[调整内容]。
这样调整可以吗？
```

直接落在契约内时，使用：

```text
你确认的第一版可以按现在的想法继续。
它会保持简单、免费，主要内容留在用户自己的电脑里。接下来我会进入制作阶段。
```

## Store and compliance checkpoints

- Microsoft Store 的 MSIX 分发由 Store 流程完成生产签名；Store 外部 MSI/EXE 路线需要发行方自行处理代码签名。参考 [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)。
- 免费 Offer 可以保持免费发布链路；只列出免费 Offer 时，Partner Center 文档说明无需填写 payout profile 或税务表单。参考 [Set up Microsoft Marketplace payout and tax profiles](https://learn.microsoft.com/en-us/partner-center/account-settings/set-up-your-payout-account)。
- Restricted Capability 会增加提交资料与审核变量；`allowElevation` 等能力进入单独评估。参考 [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)。
- 如果 App 访问、收集或传输个人信息，按 Store policy 准备隐私政策 URL 与数据说明。参考 [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)。
- 所有 Store 产品仍需完成年龄评级与 Store listing 资料。将这些步骤作为人工确认点保留在发布流程中。
