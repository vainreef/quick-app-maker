# Vainreef Quick App Maker

> **让自然语言想法在 Windows 上全自动蜕变为真实的 WinUI 3 现代桌面应用，并直通 Microsoft Store 微软应用商店。**  
> 一键自动化工具链准备 · 8层渐进式需求访谈 · 5大一次性写对黄金铁律 · 双窗口并行与名称前置验重 · 商店6大表单全套指引

---

## 架构总览：端到端全生命周期全景图

```mermaid
graph TD
    subgraph Layer0 ["0. 一键入场层 (Bootstrap Entry)"]
        E1["用户输入 gitee.com/freevian/quick-app-maker"] --> E2["entry.ps1 自动执行"]
        E2 --> E3["全自动配齐: Git + .NET 10 + WinAppCLI + WinUI 模板"]
        E3 --> E4["输出 BOOTSTRAP_READY 并移交 Skill 执行"]
    end

    subgraph Layer1 ["1. 需求与设计层 (Discovery & Design)"]
        E4 --> D1["8 层渐进式白话需求访谈 (discovery-interview.md)"]
        D1 --> D2["锁定暂定名与核心闭环，用户确认: '开始吧'"]
        D2 --> D3["遵循 WinUI 3 一次性写对 5 大黄金铁律 (防 WMC9999 / 防 0xc000027b)"]
        D3 --> D4["素材获取: 100% 国内源与系统自带 (test-assets.md)"]
    end

    subgraph Layer2 ["2. 双轨并行研发层 (Dual-Window Concurrency)"]
        D4 --> P1["启动全自动构建流水线 (约 15~20 分钟)"]
        P1 -->|窗口 1: 专注构建| W1["编码 -> 自包含 MSIX 打包 -> winapp ui 自动化黑盒测试 -> 本地安装"]
        P1 -->|友好非阻塞提示| W2["窗口 2: 独立咨询会话<br>用户提问: '如何创建 Partner 账号 / 如何起名？'"]
        W2 --> W3["读取本地 partner-center-guide.md<br>指引 Xbox 注册免验证码 -> 身份证上传 -> 免费个人开发者"]
        W3 --> W4["控制台前置验重: '+ 新产品' -> 'MSIX 或 PWA' -> 实时测名字并预留 -> 拿到 3 大 Package Identity"]
    end

    subgraph Layer3 ["3. 交付与共创层 (Deliver & Co-Create)"]
        W1 --> C1["首版交付话术: '已装好，请打开把玩，哪里不顺手随时告诉我，改到满意为止'"]
        C1 --> C2["用户本地试用体验 -> 提修改意见 -> 小步迭代重装 (严禁首版主动推销上架)"]
    end

    subgraph Layer4 ["4. 商店发布与断点续接层 (Partner Center & Store)"]
        C2 -->|用户主动提出: '我想发布到商店'| S1["启动发布链条 6 大检查项状态机"]
        W4 -.回填 3 大 Package Identity.- -> S1
        S1 --> S2["定制 1:1 专属 App Logo 与 1080P 真实运行截图"]
        S2 --> S3["winapp package 生成 Store 正式包 (清理冗余权限)"]
        S3 --> S4["参考 partner-center-guide.md 辅助用户填报 6 大表单 (定价/属性/年龄分级/包/语言物料/选项)"]
        S4 --> S5["点击'提交到应用商店' -> 24~72h 审核全球上线！"]
    end
```

---

## Agent 3 步极速开工指令

### 第一步：执行唯一公开入口
用户只要说 `用 https://gitee.com/freevian/quick-app-maker README 开始`，Agent 在 Windows PowerShell 中直接执行下面三行（使用 `main` 分支）：

```powershell
$entry = Join-Path $env:TEMP 'vainreef-quick-app-maker-entry.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

### 第二步：识别就绪输出与工作目录
一键脚本执行完毕后输出：
```text
BOOTSTRAP_READY
WORKSPACE_ROOT: <仓库的父目录>
NEXT_ACTION: read skills/vainreef-fast-publish/SKILL.md and start discovery
```

* **工作目录铁律**：所有新项目目录 `<app-slug>/`、`README.md`、临时测试、安装包全部放在 `WORKSPACE_ROOT`（与 `quick-app-maker/` 同级）。
* **仓库只读保护**：`quick-app-maker/` 是只读 Skill 知识库，**Agent 禁止任何 git add/commit/push 操作**。

### 第三步：开工前更新知识库并进入需求访谈
```powershell
git -C quick-app-maker pull --ff-only origin main
```
随后立即读取 [`skills/vainreef-fast-publish/SKILL.md`](skills/vainreef-fast-publish/SKILL.md) 与 [`references/discovery-interview.md`](skills/vainreef-fast-publish/references/discovery-interview.md)，向用户发起白话开场问询：
```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

---

## 全景文件拓扑与职责矩阵

```text
quick-app-maker/
├── README.md                                # 🏛️ 全系统总览与上帝视角架构蓝图 (本文件)
├── bootstrap/                               # 🚀 零依赖一键初始化模块
│   ├── entry.ps1                            # 公开入口：安装 Git (缺时)、clone 仓库、启动安装器
│   ├── install.ps1                          # 智能安装器：并行检测并补齐 .NET 10 / CLI / 模板
│   ├── toolchain.json                       # 唯一权威版本锁清单
│   └── workers/                             # 下载与安装子工作脚本
├── skills/vainreef-fast-publish/            # 🧠 核心业务大脑与执行规范
│   ├── SKILL.md                             # 12 大生命周期标准流程、5 大黄金铁律、双窗口规范
│   └── references/                          # 核心业务子模块与权威知识库
│       ├── discovery-interview.md           # 8 层渐进式需求访谈框架与双窗口启动通知
│       ├── test-assets.md                   # 中国大陆 100% 畅通素材库 (音频/图标/图片/字体)
│       ├── partner-center-guide.md          # 微软 Partner Center 注册、名称验重与 6 大表单全指南
│       ├── delivery-considerations.md       # 交付边界、权限、离线与性能考量
│       ├── official-sources.md              # 微软官方文档与 API 规范入口
│       └── toolchain/v1/commands.md         # 41 个实机验证避坑指南、命令硬规则、UI 自动化生命周期
├── docs/                                    # 📚 归档与实战证据库
│   ├── windows-smoke-test.md                # 1~6 轮真实 Windows 机器全流程实测战绩记录
│   └── partner-center/                      # 8 个 Partner Center 真实页面 DOM 快照与实测记录
│       ├── 微软PartnerCenter个人账户注册记录.md
│       ├── 页面快照-应用和游戏概述.html
│       ├── 页面快照-应用程序概述.html
│       ├── 页面快照-定价和可用性.html
│       ├── 页面快照-属性.html
│       ├── 页面快照-年龄分级.html
│       ├── 页面快照-程序包.html
│       ├── 页面快照-Store一览-管理语言.html
│       └── 页面快照-提交选项.html
└── apps/                                    # 💻 用户本地作品工作区 (已加入 .gitignore，绝不上传)
```

---

## 四大核心支柱机制（The 4 Pillars）

### 1. 【网络与素材支柱】中国大陆 100% 畅通红线
* **绝对红线**：开发环境 100% 位于中国境内，**严禁一切下载海外资源的尝试**（直连 GitHub releases、raw.githubusercontent、海外未镜像 API 必被墙卡死）。
* **素材黄金准则**：
  * 音效优先复制系统自带 `C:\Windows\Media\*.wav`（零网络、零下载）；
  * 图标优先使用 XAML 内置 `Segoe Fluent Icons` 字体字形与 `PersonPicture` 控件；
  * 图片使用 `test-assets.md` 已验证的国内 CDN（清华 TUNA、阿里云 OSS、Gitee 镜像、`img.scdn.io`）；
  * **搜不到国内可靠源立即彻底放弃下载，改为本地 PowerShell GDI+ 绘图或 XAML 控件拟态！**

### 2. 【编码与质量支柱】WinUI 3 一次性写对 5 大黄金铁律
* **【铁律 1】DataTemplate 必须显式声明 `x:DataType`**：凡使用 `{x:Bind}`，根节点必须声明 `x:DataType="models:Class"`。看到 `WMC9999 ErrorMessages.resources` 假死错误 100% 是漏写了 x:DataType，严禁改依赖换包！
* **【铁律 2】ContentDialog 必须设置 `XamlRoot`**：打开弹窗前必须赋予 `XamlRoot = this.Content.XamlRoot`，否则底层必抛 `0xc000027b` 原生闪退！
* **【铁律 3】计划通知标准范式**：CsWinRT 投影无 `Recurrence` 属性，每年提醒采用循环单次调度，catch 时打印 `HResult`（`0x803E0120` 为管理员会话系统限制，视为正常）。
* **【铁律 4】严禁操作 `HKLM:` 注册表与 `Cert:\LocalMachine\`**：证书只导入 `CurrentUser\TrustedPeople`，彻底杜绝 UAC 管理员提权弹窗打扰用户。
* **【铁律 5】国内 NuGet 还原极速源**：依赖还原强制指定国内 Azure CDN 源 `https://nuget.azure.cn/v3/index.json`。

### 3. 【体验与协同支柱】双窗口并行与控制台名称前置验重
* **窗口 1（构建主会话）**：需求确认后启动构建，输出一句轻量提示后**专心推进代码编写、MSIX 自包含打包与 `winapp ui` 自动化黑盒测试**，不混杂问答。
* **窗口 2（独立咨询会话）**：利用 30 分钟构建空窗期，用户新开窗口提问“如何创建 Partner 账号 / 如何起名？”，Agent 读取本地 `partner-center-guide.md`，指导用户：
  * 通过 Xbox 应用免验证码注册，开通免费个人开发者；
  * **控制台名称前置验重**：在控制台点击 `+ 新产品 -> MSIX 或 PWA 应用`，输入名称点击 **「检查可用性」**。若重名当场换名，确认可用后立即预留，拿到 3 大 `Product Identity` 参数，**从源头杜绝下游重命名推倒重来**！

### 4. 【交付与发布支柱】共创把玩优先 & 商店断点智能续接
* **首版交付心智**：第一版安装到电脑后，Agent 热情邀请用户试用：“已装在电脑上，随时可以打开把玩，哪里不顺手随时告诉我，我们继续修改直到你满意为止”，**严禁首版主动推销上架**。
* **发布时刻断点智能续接**：当用户充分把玩满意并提出上架时，Agent 启动 6 项检查状态机：
  * 若用户在窗口 2 已拿到 3 大参数，直接回填打包；
  * 若未完成，Agent 从断点处精准续接，参考 `docs/partner-center/` 快照辅助用户填报 6 大表单（定价、属性、年龄分级、程序包、Store 一览、提交选项），点击提交审核（24~72h 全球上线）！

---

## 历史实测战绩记录（Smoke Test Record）

本仓库所有命令与规则均在真实 Windows x64 机器上经历多轮完整闭环实测验证：

| 轮次 | 测试应用 | 测试结果 | 沉淀核心成果 |
| :--- | :--- | :--- | :--- |
| **轮次 1~3** | SmokeTest 基础工程 | 链路全通 | 确立自包含打包三件套、`winapp ui` 自动化黑盒测试规范 |
| **轮次 4** | 纪念日 DaysMatter | 链路全通 | 沉淀数据预置法、ContentDialog 模式，新增 7 个坑点（26~32 条） |
| **轮次 5** | RememberWhat 记得什么 | 31 分钟全通 | 解决计划通知底层投影问题，新增 4 个坑点（33~36 条），确立降噪屏障 |
| **轮次 6** | OldTimes 旧时光 | 链路全通 | 攻克 XamlCompiler `WMC9999` 根因、弹窗 `XamlRoot` 崩溃，确立 5 大黄金铁律（37~41 条） |
