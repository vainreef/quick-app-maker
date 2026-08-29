# Edge Store CLI (V2 - .NET 10 声明式状态收敛驱动)

这是一个基于 **.NET 10 (C#)** 构建的现代化、强类型、声明式 Microsoft Edge 浏览器自动化驱动，通过 Chromium DevTools Protocol (CDP) 控制隔离的 Edge 进程，实现 Microsoft Partner Center 商店提审全流程的状态收敛与验证。

执行前必须读取仓库级 [Agent 运行契约](../../docs/partner-center/Agent-运行契约.md)。`apps/Project/edge-store-cli-fast` 是旧诊断副本，不是本工具入口。

---

## 核心架构分层 (3-Layer Architecture)

```text
store-automation.json
    │
    ▼
┌──────────────────────────────┐
│      Store Orchestrator      │
│      Store 0 ~ Store 7       │  <-- 声明式全流程编排器 (Preflight / Plan / Convergence)
│   Desired / Observe / Plan   │
│  Reconcile / Verify / Submit │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│    Partner Center Adapter    │
│                              │
│     AvailabilityAdapter      │
│      PropertiesAdapter       │  <-- 6 大表单业务适配器 (Observe / Diff / Apply / ReloadVerify)
│      AgeRatingsAdapter       │
│       PackagesAdapter        │
│        ListingAdapter        │
│        OptionsAdapter        │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│     Component Adapters       │  <-- LitElement / Angular 特殊组件深度适配
│   HeSelect / HeCheckbox      │      (Shadow DOM 穿透、AX 树选项定位、中心物理点击、状态断言)
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│        Browser Driver        │
│                              │
│       CdpClient (WS)         │  <-- .NET 10 原生驱动核心 (浅根 DOM + requestNode，
│      DOM + Shadow DOM        │      Accessibility.queryAXTree, getBoxModel, InputDriver,
│      Accessibility Tree      │      契约式 Waiter 消除一切 Start-Sleep)
│   Native mouse / keyboard    │
│   wait / navigation / upload │
└──────────────┬───────────────┘
               │
               ▼
         Microsoft Edge
```

---

## 为什么从 PowerShell 升级到 .NET 10 C#

1. **类型安全与零编码陷阱**：彻底告别 PowerShell 5.1 在 GBK/ANSI 环境下的 UTF-8 乱码、`\uXXXX` 多重转义混淆、以及单元素数组被管道自动拆包的经典陷阱；
2. **有界 Shadow DOM 访问**：禁止在大 SPA 上序列化整棵 DOM；使用浅根、`Runtime.evaluate → DOM.requestNode`、组件 `shadowRoot` 与 `Accessibility.queryAXTree` 做局部定位；
3. **真实字段级 Observed State 与幂等性 Diff**：
   - 观测模型直接读取页面真实值（货币、价格段、分类、隐私文本、设备家族、特权说明）；
   - 表单相等只算中间证据；只有冷加载回读且概览模块明确完成，才输出 `PRODUCT_VERIFIED`；
4. **零新增环境依赖**：`quick-app-maker` 本身就会安装 .NET 10 SDK，直接使用系统预备的 `dotnet` 运行，无需 Node.js 或 Python。

---

## 8 阶段标准发布流水线（Store 0 ~ Store 7）

```text
STORE 0: STATIC PREFLIGHT        # 离线解包质检 MSIX (DisplayName/Desktop-only/Logo/1080P截图/关键字<=7)
    ↓
STORE 1: SESSION DISCOVERY       # 启动/复用隔离 Edge 进程，验证 CDP 连接与用户登录
    ↓
STORE 2: LIVE COMPATIBILITY PROBE# 概览页动态读取 live submissionId 与 6 大表单实时 href
    ↓
STORE 3: PLAN                    # 计算 DesiredState vs ObservedState 生成精准 Diff 计划
    ↓
STORE 4: FORM RECONCILIATION     # 逐表执行 (HeSelect 语义点击 / 原生表单修改 / 保存)
    ↓
STORE 5: RELOAD VERIFICATION     # 对当前绝对 URL 冷导航二次读取，验证服务端真正持久化
    ↓
STORE 6: SUBMISSION INTEGRITY    # 概览页确认 6 大模块均显示绿色已完成状态
    ↓
STORE 7: USER SUBMIT DELIVERY    # 自动化填报结束，移交用户仔细审核后手动点击【提交进行认证】
```

---

## 常用命令

### 0. 验证 C# 工程与配置语法
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Validate-EdgeStoreCli.ps1 -Strict
```

### 1. Store 0 离线静态质检（不打开浏览器）
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest .\<app>\build\edge-store.json
```

### 1.1 全新应用自动建项与身份回填（自动验重/预留并回填 Package.appxmanifest）
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action reserve -AppName <AppName> -Manifest .\<app>\build\edge-store.json
```

### 2. 启动隔离 Edge 会话
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -Manifest .\<app>\build\edge-store.json -KeepOpen
```

### 2.1 只读查看当前页面（不导航、不填表）
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action inspect -Manifest .\<app>\build\edge-store.json
```

`status` 同时返回 PID/Port、checkpoint 和当前结构化页面状态。不要把 `run -Phase all` 当检查命令。

### 3. 离散单表收敛（默认发布路径）
```powershell
# 按表逐项收敛：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase availability -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase properties   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase ageRatings   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase packages     -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action cleanlanguages -Manifest .\<app>\build\edge-store.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action filllisting -Manifest .\<app>\build\edge-store.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action inspectoptions -Manifest .\<app>\build\edge-store.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action filloptions -Manifest .\<app>\build\edge-store.json
```

`run -Phase all` 保留为显式六阶段兼容入口；它会执行写操作，发布主路径仍按上面的离散命令逐步审查。

### 4. 提审交付（由用户手动点击）
- 概览页 6 大模块全绿总检通过后，自动化填报全部结束；
- Agent 向用户汇报：“自动填报已结束，请仔细审核后，点击 提交进行认证 按钮”；
- 最终提交必须且只能由用户在已打开的浏览器中亲自审核后手动点击【提交进行认证】。

---

## 诊断与修复辅助动作

以下动作与 `Invoke-EdgeStore.ps1` 当前 `ValidateSet` 保持一致，用于只读诊断或单点清理：

### dumpdom —— 抽取当前页面清洗后的 DOM
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action dumpdom -Manifest .\<app>\build\edge-store.json -StateDir .\<cache>\state
```
- 产物：`dom-dump-LIVE.html`（写在 manifest 所在目录），只保留**按钮/表单控件 + 位置(所在行文本) + 状态(checked/disabled/selected)**，剥离导航、AI 助手、隐藏弹窗与整段说明。还附带 he-select/he-checkbox 主机清单、文件输入、定价区容器、提审行原始 HTML。

### fulldom / diagnoselisting —— 深度页面诊断
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action fulldom -Manifest .\<app>\build\edge-store.json -StateDir .\<cache>\state
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action diagnoselisting -Manifest .\<app>\build\edge-store.json -StateDir .\<cache>\state
```
- `fulldom` 输出完整原始页面证据；`diagnoselisting` 聚焦 Listing 的语言、折叠区、关键词和上传控件。

### cleanpackages —— 清理冲突程序包
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action cleanpackages -Manifest .\<app>\build\edge-store.json -StateDir .\<cache>\state
```
- 清理 `Error`、重复或冲突行后，再单独执行 packages 阶段上传；清理动作本身不代表上传完成。

每次显式传入 `-StateDir <工作区>/.cache/edge-store-state`，把会话、日志和 checkpoint 固定在工作区缓存目录。

---

## 实测校准出的页面行为（重要，勿改回）

以下是对当前 Partner Center 实页（2026-08 实测）校准过的行为，写成规则供后续 Agent 遵守：

1. **概览模块完成判定**：当前 UI 模块行有 `<app-module-status>`，完成态**不出现任何标签**；未完成态出现 `未启动` 徽标。判定规则：状态纹含 `未启动/not started` → Incomplete；否则（无徽标）→ Complete。**不要**寻找 `complete/checkmark/已验证` 等关键词。
2. **冷加载**：`Page.reload`(F5) 会把 SPA 刷新成空白壳。持久化校验必须用**冷导航**（对当前 URL 再 `Page.navigate`），否则 `CategorySelect`/表单控件读不到。
3. **页面识别**：表单信号用「控件是否存在」判定（`hasAny`），而不是「控件可见」。因为单选框/复选框多为自定义样式、原生控件隐藏（宽高为 0），只看可见会误判为 SubmissionOverview。
4. **表单被误判为 SubmissionOverview**：只有 URL 含 `/overview` 才视为「已重定向到概览」。阶段页（`/availability` 等）在 SPA 加载期被误判为 SubmissionOverview 时，不得走概览分支。
5. **新建产品入口**：`+ 新产品` 位于 `https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview`（不是 `/products/apps-and-games`，后者 404）。按钮在 Shadow DOM 内，需穿透定位。
6. **Package Identity 取值**：身份值在「标签」所在 `<tr>` 的**第二个 `<td>`**（`<td class="identity.Name">`），且值比标签晚渲染。须等待值单元格出现再读，且读第 2 个 `<td>`。
7. **产品声明复选框**：用 `name`（如 `'storage-checkbox'`、`'windows-checkbox'`、`'usesGenAI-checkbox'`）定位，不要用 `windows` 这类模糊文本。
8. **年龄分级答案**：每题 2 个单选 `input[name="question#<id>"]`，答案文本在 label 里的 `<span class="response-text">是/否</span>`。选「否」时用 `.response-text` 精确匹配，且**先 `scrollIntoView` 再测坐标**（题在页下方，不滚会误判越界）。
9. **上传文件**：上传控件的 `input[type=file]` 是隐藏/Shadow DOM 内的原生输入。用 `Runtime.evaluate` 穿透 Shadow DOM 解析其 `objectId`，再用 `DOM.setFileInputFiles(objectId, ...)`。**不要**用 `DOM.requestNode`（对隐藏/Shadow 输入会失败）。清单、列表页图片上传同样适用。
10. **清单/Logo 资源**：`winapp package ./publish` 只打包发布目录内容；若 manifest 引用 `Assets\StoreLogo.png` 等，先精确重建 `publish\Assets\`，避免缺图和 `Assets\Assets` 嵌套。
11. **失败包清理**：同名包出现 Error 行会阻止再上传。先在 `/packages` 点 `Delete`（`a.upload-action`），再重传修正后的包。包校验大包（WindowsAppSDK 自包含）会「Analyzing package」较久，且 `runFullTrust` 受限能力需在「提交选项」填用途说明获批。
12. **每步操作读日志**：所有导航/等待/点击/输入/选择/勾选都会打 `[HH:mm:ss.fff] [级别] ...` 到控制台并追加到 `<state>/logs/ops.log`，便于定位卡点。

---

## 退出码约定 (Exit Codes)

| 退出码 | 含义 |
| ---: | --- |
| 0 | 请求阶段已由产品概览验证 (`PRODUCT_VERIFIED`) |
| 1 | 执行异常或验证未收敛 |
| 2 | 配置文件路径或 JSON 语法错误 |
| 3 | .NET 10 SDK 或 Edge 可执行文件未找到 |
| 4 | 只读计划发现差异；未执行 Apply，未记录为完成 |

## 完成语义

`按钮点击成功`、`DOM 值相等`、`保存后仍在当前页`、`EXIT=0` 都不是产品完成的充分条件。阶段状态依次为：

`Observed → NeedsChanges → Applying → AppliedUnverified → Converged/Failed`

只有 `Converged` 会写入 `convergedPhases`，并且必须保存概览页证据。单表续跑直接使用 checkpoint 中的 submissionId，不重跑 preflight，也不先遍历其他表。
