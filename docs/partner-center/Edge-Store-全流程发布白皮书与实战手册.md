# Edge Store 自动化发布全流程白皮书与终极实战手册

本文档为 **Windows 桌面应用（MSIX / WinUI 3 / WPF）通过 Edge Store CLI 自动化上架微软开发者中心（Microsoft Partner Center）** 的全流程终极权威指南。

所有经验与代码均来自真实线上实战（以「牵挂EXXE」`9NCDR7XD30XJ` 成功提审入库为标准基线），涵盖 10 大离散发布阶段、CDP 底层交互原理、Shadow DOM 穿透方案与 74 个经典踩坑防范铁律。

---

## 目录
1. [系统整体架构与分层设计](#1-系统整体架构与分层设计)
2. [发布流水线 10 大离散阶段规范](#2-发布流水线-10-大离散阶段规范)
3. [核心表单攻坚与避坑铁律](#3-核心表单攻坚与避坑铁律)
   - [3.1 程序包上传与概览页唯一真值确权（Stage 6）](#31-程序包上传与概览页唯一真值确权stage-6)
   - [3.2 Store 一览双层结构与 80+ 多语言修剪（Stage 7）](#32-store-一览双层结构与-80-多语言修剪stage-7)
   - [3.3 提交 ID 三级容灾解析机制](#33-提交-id-三级容灾解析机制)
4. [CDP 与 Lit / Web Components 交互规范](#4-cdp-与-lit--web-components-交互规范)
5. [CLI 命令矩阵与参数速查](#5-cli-命令矩阵与参数速查)

---

## 1. 系统整体架构与分层设计

Edge Store 自动化工具链（`toolchain/edge-store-cli/`）采用**纯轻量、单向依赖、高内聚命令模式（Command Pattern）**构建：

```text
                                  Invoke-EdgeStore.ps1 (PowerShell 入口)
                                                  │
                                                  ▼
                                      Program.cs (参数路由 < 300 行)
                                                  │
       ┌──────────────────┬───────────────────────┼───────────────────────┬──────────────────┐
       ▼                  ▼                       ▼                       ▼                  ▼
 PreflightCommand   ReserveCommand        PackageCleanerCommand    LanguageGridCleaner   FillListingCommand
 (离线静态质检)     (产品名称预留/身份回填) (包清理/Validated/确权)   (80+多语言删除持久化) (中文详情表单与素材)
       │                  │                       │                       │                  │
       └──────────────────┴───────────────────────┼───────────────────────┴──────────────────┘
                                                  ▼
                                     StoreOrchestrator (流水线编排)
                                                  │
                      ┌───────────────────────────┴───────────────────────────┐
                      ▼                                                       ▼
           Component Adapters                                            Cdp Engine
   (Availability, Properties, AgeRatings,                      (CdpConnection, CdpClient, DomDriver,
       Packages, Listing, Options)                                InputDriver, AxLocator, PageInspector)
```

- **单单体拆分铁律**：主入口 `Program.cs` 严格限制在 300 行以内，仅负责 CLI 参数解析与命令分发。每个复杂业务阶段封装为独立的 Command 类，严禁历史 ad-hoc 代码污染主干。
- **全量无转义中文输出**：JSON 序列化全面启用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，确保控制台输出清晰可读的中文结构化快照。

---

## 2. 发布流水线 10 大离散阶段规范

提审流程**严禁一个脚本一冲到底**，必须严格按以下离散阶段推进，每阶段执行完毕后必须通过 DOM 探针进行阶段自检：

| 阶段代号 | 阶段名称 | 核心动作 | CLI 命令 |
| :--- | :--- | :--- | :--- |
| **Stage -1** | 自动建项与身份回填 | 访问 `apps-and-games/overview`，预留名称并抓取 3 大核心包身份回填 Manifest | `-Action reserve -AppName "..."` |
| **Stage 0** | 静态离线预检 | 质检描述字数(300+)、5大特性、7关键词、MSIX架构与Assets资源完整度 | `-Action preflight` |
| **Stage 1** | 常驻浏览器启动 | 在 `WinSta0\Default` 桌面拉起独立 Edge 会话并确权登录态 | `-Action launch -KeepOpen` |
| **Stage 2** | 动态 Submission 探测 | 访问产品主页，动态解析当前活跃 `SubmissionId` 与 6 大表单直达 URL | `-Action discover` |
| **Stage 3** | 定价与可用性 | 设定免费价格等级（`0`）、中国区市场与分发选项，保存草稿并自检 | `-Action step -Phase availability -Apply` |
| **Stage 4** | 属性与隐私政策 | 设定分类为 `Productivity`、离线免责文本，保存草稿并自检 | `-Action step -Phase properties -Apply` |
| **Stage 5** | 年龄分级问卷 | 自动填写 IARC 调查问卷（非暴力/离线工具），生成评级徽标并保存 | `-Action step -Phase ageRatings -Apply` |
| **Stage 6** | 程序包上传与确权 | 穿透上传 MSIX，清理历史残余包，确权 `Validated` 并退回概览页验绿勾 | `-Action cleanpackages` 或 `-Action step -Phase packages -Apply` |
| **Stage 7** | Store 一览双层填报 | ①在 `managelanguages` 批量删除 80+ 冗余语言并保存网格；②进入 `中文(中国)` 表单填报 302 字描述与截图 | `-Action cleanlanguages` 接着 `-Action filllisting` |
| **Stage 8** | 提审选项 | 先只读提取现场状态，审查后配置发布模式与受限功能理由并保存 | `-Action inspectoptions` 后执行 `-Action filloptions` |
| **Stage 9** | 概览页 6 大模块总检 | 强制冷加载概览页，断言 6 大模块全绿勾且无「未启动」/「未完成」 | `-Action verify` |
| **Stage 10** | 用户提交交付 | Agent 汇报六大模块证据，用户在浏览器中复核并点击「提交进行认证」 | 用户手动操作 |

---

## 3. 核心表单攻坚与避坑铁律

### 3.0 自动建项与官方工作区路由铁律（Stage -1）
- **核心踩坑**：
  Partner Center 是 SPA 架构，不存在独立的 `/products/create` 物理路由或公开直达端点，直接跳转会遭遇 404 及 Akamai CDN 的 `Access Denied`（坑 76）。此外，`+ 新产品` 按钮需等待异步权限加载后才挂载到 Shadow DOM（坑 77）。
- **铁律**：
  1. 必须导航至官方总览工作区 `https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview`；
  2. 使用带 30 秒弹性等待的 Shadow DOM 深度扫描器探测 `+ 新产品` 并点击；
  3. 选择 `MSIX 或 PWA 应用` 后填入应用名称并点击「检查可用性」与「保留产品名称」；
  4. 自动跳转到 `/identity` 抓取真实 Package Identity 并反向回填到项目 `Package.appxmanifest` 与清单文件中。

### 3.1 程序包上传与概览页唯一真值确权（Stage 6）
- **核心踩坑**：页面表格显示 `Validated` 绝不等于该模块已保存完成！如果页面存在历史重复行或未点「保存」，草稿依然为 `Incomplete`。
- **铁律**：
  1. 上传后必须调用 `cleanpackages` 清理同名或报错包体；
  2. 显式选中 Target Device Family（`Windows.Desktop`）；
  3. 点击页面底部的「保存」；
  4. **必须强制导航回概览页（Overview Page），由 DOM 探针切实检测到 `<app-module-status>` 显示为「完成」，才允许放行进入下一阶段！**

### 3.2 Store 一览双层结构与 80+ 多语言修剪（Stage 7）
- **双层架构原理**：
  - **层级 1：语言管理网格（`managelanguages`）**：MSIX 内置的本地化字符串会被微软默认全量导入为 80~100+ 种激活语言。
    - **处理方式**：通过 `he-button.shadowRoot` 派发 `composed` 点击事件，异步逐项将所有非中文行标记为 `已删除`（仅保留 `中文(中国)`）。
    - **第一层持久化**：必须在 `managelanguages` 页面底部**物理点击「保存」按钮**，使语言裁剪草稿在服务器端生效！
  - **层级 2：语言详情表单（`listings?languageid=5&languagecode=zh-cn`）**：
    - 精准定位官方命名 **`中文(中国)`**（`languageid=5&languagecode=zh-cn`）；
    - 填入 300+ 字完整应用描述（严禁短小空洞）、简短描述、5 项核心功能特性、7 个搜索关键词；
    - 穿透 Shadow DOM 上传 1920x1080 桌面高清截图与 1:1、300x300 图标；
    - **第二层持久化**：物理点击表单底部的「保存」按钮！
  - **终检确权**：返回概览页，冷加载验证 `Store 一览` 呈现绿勾「完成」。

### 3.3 提交 ID 三级容灾解析机制
- **核心踩坑**：清单文件作为模板可能未预填运行时生成的提交 ID，直接拼接 URL 会产生 `.../submissions//listings` 双斜杠 404 错误。
- **三级容灾方案**：
  1. `desired.SubmissionId`（若非空）；
  2. 自动读取本地 `checkpoint.json` 中的 `SubmissionId`；
  3. 自动调用 `SubmissionDiscovery` 探针访问产品主页实时提取并持久化。

---

## 4. CDP 与 Lit / Web Components 交互规范

微软 Partner Center 深度采用 Web Components 与 Shadow DOM，自动化操作必须遵循以下底层法则：

1. **点击穿透法则**：
   对于 `<he-button>` 等自定义组件，常规 `element.click()` 无法触发组件内部的数据绑定。必须穿透 `shadowRoot` 找到内部原生 `<button>` 并派发完整鼠标事件：
   ```javascript
   if (btn.shadowRoot) {
       const inner = btn.shadowRoot.querySelector('button');
       if (inner) inner.click();
   }
   btn.click();
   btn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
   ```
2. **文件上传穿透法则**：
   使用递归深度收集所有 Shadow Root 内部的 `input[type="file"]`，通过 CDP `DOM.describeNode` 获取 `backendNodeId`，再调用 `DOM.setFileInputFiles` 穿透设值。
3. **导航绝对安全法则**：
   严禁使用 `a[href*="/overview"]` 等模糊选择器（防止误点微软 Learn 文档等外链导致跳出控制台）。必须使用 `${baseUrl}/${productId}/overview` 绝对构造的控制台 URL 强制跳转。

---

## 5. CLI 命令矩阵与参数速查

```powershell
# 1. 静态预检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest .\qiangua\build\edge-store-EXXE.json

# 2. 启动常驻 Edge 并确权
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -KeepOpen

# 3. 提取 100% 完整原始 DOM 与可见文本 (排障利器)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action fulldom -StateDir .\.cache\edge-store-state

# 4. 程序包清理与概览页确权
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action cleanpackages -Manifest .\qiangua\build\edge-store-EXXE.json -StateDir .\.cache\edge-store-state

# 5. 多语言网格批量精简 (删除 80+ 冗余语言并保存)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action cleanlanguages -Manifest .\qiangua\build\edge-store-EXXE.json -StateDir .\.cache\edge-store-state

# 6. 中文 Store 一览表单填报与截图上传
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action filllisting -Manifest .\qiangua\build\edge-store-EXXE.json -StateDir .\.cache\edge-store-state

# 7. 概览页 6 大模块冷加载总检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action verify -Manifest .\qiangua\build\edge-store-EXXE.json -StateDir .\.cache\edge-store-state

# 8. 提交交付
# verify 通过后，Agent 汇报现场证据；用户在已打开的浏览器中复核并点击「提交进行认证」。
```

---

## 6. 如何删除产品（用户手动操作指引与安全红线）

> ⚠️ **安全红线**：删除产品属于高风险且不可逆动作，**绝对严禁 Agent 通过自动化脚本代为删除**。如果需要删除产品，必须指引用户在网页端自行操作。

### 用户手动删除产品标准指引：
1. **删除草稿提交（前置）**：
   - 若当前产品存在正在草拟的提交（Draft Submission），必须先在产品页面点击 **「删除提交」** 按钮，使产品回到无草稿状态。
2. **进入应用程序概述页面**：
   - 访问 `https://partner.microsoft.com/zh-cn/dashboard/products/<ProductId>/overview`。
3. **点击下拉菜单触发器**：
   - 点击右上角或标题旁的更多操作图标按钮（DOM 结构：`<a slot="trigger" class="dropdown-toggle" id="he-dropdown-button-1"><he-icon name="more"></he-icon></a>`）。
4. **点击「删除产品」**：
   - 在弹出的下拉浮层中，点击 **「删除产品」**（DOM 结构：`<span class="delete-draft">删除产品</span>`）。
5. **二次弹窗确认**：
   - 在系统确认对话框中点击确定，即可彻底从 Partner Center 中删除该产品。
6. **刷新页面验证（⚠️ 关键注意点）**：
   - Partner Center 属于单页应用（SPA），删除操作提交后前端可能存在局部视图缓存，**必须手动按 F5 刷新（或强制刷新）页面**，产品列表中才会同步更新并消失。

