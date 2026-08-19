# Progressive Discovery Interview

## Goal

把用户最初的模糊想法收敛成一份可确认、可实现、可验收的 V1 产品合同。访谈采用逐步递进方式：每轮解决当前最关键的不确定点，再根据答案选择下一问。

## Conversation rules

- 每轮优先问一个关键问题；紧密关联的问题最多组合两个。
- 先谈用户和问题，再谈功能；先谈主流程，再谈边缘场景；最后进入技术与发布边界。
- 使用用户已经给出的词汇和例子。
- 提供简短示例帮助用户回答，同时保留自由描述空间。
- 用户说得抽象时，请他描述一次真实使用过程。
- 用户给出一长串功能时，先找出“最重要的一次使用”，再排序 V1。
- 每轮更新 requirement snapshot，只追问缺失或矛盾的信息。
- 技术判断放在需求理解之后，避免让技术词提前限制创意。

## Opening question

当用户只给出一句模糊想法时，从这一问开始：

```text
先从最重要的一点开始：你希望这个 App 帮谁解决什么麻烦？可以描述一次真实场景，比如“我每天下班前会……，现在最麻烦的是……”。
```

用户已经说清问题时，直接进入下一层缺口。

## Question ladder

### Stage 1 — Problem and value

目的：明确 App 存在的理由。

可选问题：

- 你最希望它替你省掉哪一步麻烦？
- 现在你通常怎么解决？最难受的地方是什么？
- 如果这个 App 今天就做好，你第一次会拿它做什么？
- 最重要的结果是什么：更快、更省事、更准确、更好看，还是更容易坚持？

完成条件：能用一句话说清“给谁、解决什么、带来什么结果”。

### Stage 2 — User and scenario

目的：明确使用者、频率和环境。

可选问题：

- 主要是你自己使用，还是希望其他普通用户也安装？
- 用户会每天用、偶尔用，还是只在某种场景打开？
- 使用时通常有哪些现成材料，比如照片、文本、CSV、文件夹或 API key？
- 用户的 Windows 操作水平大概怎样？

完成条件：明确主要用户、使用频率和典型环境。

### Stage 3 — Core journey

目的：把功能列表变成可执行流程。

核心提问：

```text
请从“打开 App”开始讲一遍：用户第一眼看到什么，接着点什么或输入什么，最后得到什么结果？
```

追问：

- 最常用的主按钮是什么？
- 输入从哪里来？结果显示在哪里或保存到哪里？
- 完成以后用户下一步通常做什么？
- 出错或没有数据时，希望 App 怎么提示？

完成条件：得到从打开到结果的完整 happy path，以及至少一个空状态/错误状态。

### Stage 4 — V1 scope and priority

目的：把第一版压成完整闭环。

可选问题：

- 如果第一版只能保留三项功能，你会选哪三项？
- 哪项功能缺失时，这个 App 就失去价值？
- 哪些想法可以等上架以后再增加？
- 你希望第一次发布更偏向“极简稳定”还是“功能丰富”？

完成条件：形成 Must / Later 两组功能，并有明确优先级。

### Stage 5 — Data and persistence

目的：明确输入、输出、存储和数据量。

可选问题：

- App 会保存哪些内容？关闭重开后还要保留什么？
- 数据来自用户输入、文件、公开 API，还是设备能力？
- 数据只在这台电脑使用，还是希望跨设备同步？
- 大概会有几十条、几千条，还是大量图片/视频？
- 用户需要导入、导出、备份或删除全部数据吗？

完成条件：选定 JSON、SQLite、文件或受控网络数据；明确数据生命周期。

### Stage 6 — Network, account, and secrets

目的：识别服务器边界。

可选问题：

- 核心功能离线时是否仍可使用？
- 如果需要网络，访问哪个服务，谁提供 API key？
- 用户是否需要注册登录？
- 是否需要云同步、多人共享、远程通知或开发者统一额度？
- API key 是每个用户自己的，还是项目方统一承担？

完成条件：明确公开 API、用户自有凭据或 Advanced Mode server boundary。

### Stage 7 — Files, permissions, privacy, and compliance

目的：识别 Store 与权限变量。

可选问题：

- App 需要用户选择哪些文件或文件夹？
- 是否需要定位、摄像头、麦克风、通知、后台运行或管理员权限？
- App 是否会把用户内容发送到远程服务？
- 是否包含账号资料、通讯录、健康、金融、未成年人或其他敏感内容？
- 功能是否依赖某种行业牌照、公司主体或第三方版权授权？

完成条件：列出普通权限、受控权限、个人信息数据流和合规前提。

### Stage 8 — Monetization and Store plan

目的：确认免费发布盒子。

可选问题：

- V1 是否确认作为免费 App 发布？
- 是否计划广告、订阅、IAP、License 或付费下载？
- 商店展示名称是什么？主要面向哪些语言和地区？

完成条件：V1 价格与 Store 目标明确；商业化需求已分类。

### Stage 9 — Name, tone, and visual direction

目的：让 README 和后续 UI 有统一方向。

可选问题：

- App 暂定叫什么？希望中文名还是英文名？
- 希望感觉偏简洁、专业、可爱、安静还是有趣？
- 有没有喜欢或排斥的颜色、图标或界面例子？
- 第一次打开时最希望用户看到哪句话？

完成条件：得到 App name、slug、语言和基本视觉方向。

### Stage 10 — Acceptance criteria

目的：定义“完成”。

可选问题：

- 你亲自测试时，会按哪几步判断它已经好用？
- 哪三个结果必须稳定成功？
- 哪些错误提示必须清楚？
- Store 首版上架前，最希望重点检查什么？

完成条件：写出可逐条勾选的验收标准。

## Requirement snapshot

访谈中维护以下结构：

```yaml
app_name: ""
app_slug: ""
one_sentence_value: ""
target_user: ""
real_scenario: ""
core_journey: []
must_have: []
later: []
inputs: []
outputs: []
storage: ""
network: ""
api_credentials_owner: ""
account_and_sync: ""
permissions: []
personal_data_flow: ""
compliance_notes: []
monetization: "Free"
visual_direction: ""
acceptance_criteria: []
fast_mode_classification: ""
projection: ""
user_confirmed: false
```

## Exit criteria

同时满足以下条件后结束访谈：

- 一句话价值主张清楚。
- 目标用户和真实场景清楚。
- 主流程从打开 App 到结果完整。
- Must / Later 功能已排序。
- 输入、输出、保存方式和数据量已确定。
- 网络、API key 所有者、账号和同步需求已确定。
- 文件、权限、隐私和行业前提已检查。
- V1 免费发布目标已确认。
- 名称或可用的暂定名已确定。
- 验收标准可以逐条测试。
- Fast Mode classification 与 projection 已形成。
- 用户确认需求复述；涉及 projection 时，用户确认修改版本。

## Final confirmation message

```text
我把你的想法整理成下面这份 V1：

- 给谁用：[target user]
- 解决什么：[problem]
- 核心流程：[journey]
- 第一版功能：[must-have]
- 数据与网络：[data/network]
- 完成标准：[acceptance criteria]

这是我对你想法的完整理解。我的理解对吗？确认后我会判断 Fast Mode 实现方式，并创建项目文件夹和 README。
```
