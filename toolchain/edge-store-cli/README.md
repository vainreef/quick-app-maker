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
STORE 5: RELOAD VERIFICATION     # 强制 F5 刷新页面二次读取，验证服务端真正持久化
    ↓
STORE 6: SUBMISSION INTEGRITY    # 概览页确认 6 大模块均显示绿色已完成状态
    ↓
STORE 7: EXPLICIT SUBMIT         # 必须显式传入 -Submit -ConfirmSubmit 触发终审
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

### 3. 单表/全表声明式收敛（含 F5 刷新二次持久化校验）
```powershell
# 按表逐项收敛：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase availability -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase properties   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase ageRatings   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase packages     -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase listing      -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase options      -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 或整链一键收敛：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
```

### 4. 显式最终提交
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -Submit -ConfirmSubmit
```

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
