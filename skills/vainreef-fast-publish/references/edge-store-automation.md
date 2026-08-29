# Partner Center 声明式应用商店自动化规范 (Edge Store Automation V2)

## 1. 架构定位与执行栈

统一入口是：

```text
toolchain/edge-store-cli/Invoke-EdgeStore.ps1 (超薄启动器)
└── dotnet run --project toolchain/edge-store-cli/EdgeStore.Cli.csproj (C# .NET 10 核心驱动)
```

本项目执行层全面基于系统已预装的 **.NET 10 (C#)** 构建，彻底废除 PowerShell 承担 CDP 浏览器驱动的设计，消灭字符编码、管道数组拆包与多重转义陷阱。

---

## 2. 三层解耦架构 (3-Layer Architecture)

```text
store-automation.json
    │
    ▼
┌──────────────────────────────┐
│      Store Orchestrator      │
│      Store 0 ~ Store 7       │  <-- 声明式全流程编排器 (Preflight / Plan / Reconcile)
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
│       CdpClient (WS)         │  <-- .NET 10 原生驱动核心 (浅 DOM + requestNode，
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

## 3. 真正的 Observed State 与 Diff 驱动的幂等性

### 3.1 字段级 Observed State
Observed State 不只是拓扑 URL，而是页面中真实的字段数据对象：
```json
{
  "submissionId": "1152921505701725275",
  "availability": {
    "allMarkets": true,
    "audience": "Public",
    "currency": "CNY - 中国",
    "priceTier": "0",
    "releaseSchedule": "string:asap",
    "stopSelling": "string:auto-fill"
  },
  "properties": {
    "category": "Productivity",
    "privacy": "No",
    "privacyPolicyText": "...",
    "storage": true,
    "backups": true,
    "windows": true,
    "usesGenAI": false
  }
}
```

### 3.2 收敛回路与幂等性
状态机核心运转公式：
$$\text{Desired} \to \text{Observed} \to \text{Plan} \to \text{Apply} \to \text{Cold Re-Observe} \to \text{Overview Complete} \to \text{PRODUCT\_VERIFIED}$$

- **第一次运行**：发现差异并执行收敛；
- **第二次运行**：`0 differences` 仍需概览模块 Complete；只有产品证据通过才输出 `PRODUCT_VERIFIED`。

---

## 4. Shadow DOM 穿透与 Accessibility Tree 语义化定位

Partner Center 的 Angular/LitElement Web Components（如 `he-select`, `he-option`）将选项内容包裹在 Shadow DOM 内，传统 `querySelectorAll` 无法获取文本。

**驱动解决方案**：
1. **有界 Shadow 访问**：大 SPA 只取浅根，按目标用 `Runtime.evaluate → DOM.requestNode`，组件内部按需访问 `shadowRoot`；
2. **Accessibility Tree 语义定位**：调用 `Accessibility.queryAXTree` 依角色与无障碍名称查找节点：
   ```csharp
   await locator.FindByRoleAndNameAsync("option", "CNY - 中国");
   ```
3. **盒模型与视口物理点击**：
   `ResolvedNode` ➔ `DOM.scrollIntoViewIfNeeded` ➔ `DOM.getBoxModel` ➔ 计算视口 CSS 中心 ➔ `Input.dispatchMouseEvent`。

---

## 5. 组件级适配器 (`HeSelectAdapter`) 标准流程

```text
Locate he-select host
    ↓
Observe current value (getAttribute('value') || innerText)
    ↓
Already correct? ➔ Return (Skip)
    ↓
Scroll into view & Native Click host (Open popup)
    ↓
WaitUntil(AXTree has role=option, name=desiredOption)
    ↓
Native Click option
    ↓
WaitUntil(Popup closed)
    ↓
Observe value again & Assert(observed == desired)
```

---

## 6. 契约式 Waiter：彻底消灭 `Start-Sleep`

禁止使用固定的 `Start-Sleep -Seconds 2/3`。全部替换为基于条件的 `WaitUntilAsync`：
- `WaitUntil(PageContract.Ready)`
- `WaitUntil(OptionPopup.Visible)`
- `WaitUntil(SaveButton.Enabled)`
- `WaitUntil(SaveCompleted)`

超时时间（如 30s/45s）仅作为最长保护上限，一旦满足条件立即毫秒级向下执行。

---

## 7. 标准自动填报与用户提交流水线（Store -1 ~ Store 7）

```text
STORE -1: PRODUCT & IDENTITY     # 自动建项验重并提取 3 大 Identity 回填源码 (Invoke-EdgeStore.ps1 -Action reserve)
    ↓
STORE 0: STATIC PREFLIGHT        # 离线解包质检 MSIX (DisplayName/Desktop-only/Logo/1080P截图/关键字<=7)
    ↓
STORE 1: SESSION DISCOVERY       # 启动/复用隔离 Edge 进程，验证 CDP 连接与用户登录
    ↓
STORE 2: LIVE COMPATIBILITY PROBE# 概览页动态读取 live submissionId 与 6 大表单实时 href
    ↓
STORE 3: PLAN                    # 对比目标配置与当前表单状态生成差异收敛计划
    ↓
STORE 4: FORM RECONCILIATION     # 逐表执行 (HeSelect 语义点击 / 原生表单修改 / 保存)
    ↓
STORE 5: RELOAD VERIFICATION     # 显式 URL 导航冷加载页面二次读取，验证服务端真正持久化
    ↓
STORE 6: SUBMISSION INTEGRITY    # 概览页确认 6 大模块均达到 Complete 状态 (无未启动徽章)
    ↓
STORE 7: USER SUBMIT DELIVERY    # verify 输出全绿证据后，移交用户在浏览器中复核并点击提交
```

---

## 8. 原地急救与排障诊断动作 (Diagnostic Actions)

当特定表单因网络波动、历史冲突包或微前端偶发异常受阻时，使用 launcher 当前公开的诊断动作，避免盲目重跑全流程：

- **`dumpdom`**：清洗并导出结构化控制台 DOM 快照（`dom-dump-LIVE.html`），快速排查控件选择器；
- **`fulldom` / `diagnoselisting`**：提取完整页面或 Listing 专项证据；
- **`cleanpackages`**：清理冲突包行，随后由 packages 阶段重新上传并等待唯一 `Validated`；
- **`probelanguages` / `cleanlanguages`**：探测和清理语言网格；
- **`inspectoptions`**：只读提取提交选项现场状态；
- **`filloptions`**：原地填写提交选项全信任说明并保存。
