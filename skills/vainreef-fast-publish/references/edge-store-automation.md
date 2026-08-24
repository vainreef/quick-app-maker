# Partner Center Edge 命令行自动化规范 (Edge Store Automation)

## 使用入口与定位

统一入口是：

```text
toolchain/edge-store-cli/Invoke-EdgeStore.ps1
```

这是纯 PowerShell 5.1 CLI，运行于隔离的 Microsoft Edge 进程中，通过本机回环地址 (`127.0.0.1`) 上的 Chromium DevTools Protocol (CDP) 执行**声明式状态收敛**、文件上传、动态 URL 探测、原生事件交互与阶段校验。

Agent **严禁使用脆弱的硬编码绝对坐标，但允许基于 DOM 元素实时 `getBoundingClientRect()` 计算几何中心派发的 CDP 原生鼠标/键盘事件**（驱动 Angular 自定义组件必需）。

---

## 核心设计哲学：声明式状态收敛 (Declarative State Convergence)

不采用盲目的“命令式脚本填表”，而是采用三层数据模型驱动的状态收敛系统：

1. **Desired State（期望状态）**：`store-automation.json` 中声明的目标（免费、生产率分类、中文描述、1080P 截图等）；
2. **Observed State（观测状态）**：实时从产品概览页 DOM 动态探测当前有效 `submissionId` 与 6 大表单的最新 `href`；
3. **Reconcile Loop（收敛回路）**：计算差异，通过原生 CDP 输入修改表单，保存后执行 **F5 Reload 验证**，确保服务端已完全接收持久化。

---

## 标准 8 阶段发布流水线 (Store 0 ~ Store 7)

```text
STORE 0: STATIC PREFLIGHT        # 离线解包质检 MSIX (DisplayName/Desktop-only/Logo/1080P截图/关键字<=7)
    ↓
STORE 1: SESSION DISCOVERY       # 启动/复用隔离 Edge 进程，验证 CDP 连接与用户登录
    ↓
STORE 2: LIVE COMPATIBILITY PROBE# 概览页动态读取 live submissionId 与 6 大表单实时 href
    ↓
STORE 3: PLAN                    # 对比目标配置与当前表单状态生成差异收敛计划
    ↓
STORE 4: FORM RECONCILIATION     # 逐表执行 (等待关键控件就绪 -> CDP 物理点击填报 -> 保存)
    ↓
STORE 5: RELOAD VERIFICATION     # 刷新页面二次验证持久化状态，无 visible errors
    ↓
STORE 6: SUBMISSION INTEGRITY    # 概览页确认 6 大模块均显示绿色已完成状态
    ↓
STORE 7: EXPLICIT SUBMIT         # 必须显式传入 -Submit -ConfirmSubmit 触发终审
```

---

## 交互原语与 Angular 自定义组件规范

Partner Center 大量使用 Angular 自定义组件（`he-select`, `he-option`, `he-checkbox` 等），直接通过 DOM `setAttribute` 或 `value=...` 不会触发内部表单状态响应，导致保存按钮保持 `DISABLED`。

**标准交互规范**：
1. **下拉框选择 (he-select)**：
   * 通过 CDP 原生鼠标点击展开下拉菜单（`market-group price-tier-selection he-select` 几何中心）；
   * 等待选项列表渲染可见（`offsetParent !== null`）；
   * 通过 CDP 原生鼠标点击目标选项（如文本为 `'0'` 的 `he-option`）；
   * 下拉关闭，Angular 内部表单状态被正确置为 valid。
2. **复选框选择 (he-checkbox)**：
   * 检查当前 `checked` 状态与期望状态；
   * 若不一致，定位元素几何中心派发 CDP 原生点击事件。
3. **JS 字符串编码红线**：
   * 传给 CDP `Runtime.evaluate` 的 JavaScript 字符串中，中文字符**一律使用 `\uXXXX` Unicode 转义**，外层使用单引号包裹，杜绝在中文 Windows GBK 环境下破坏语法。

---

## 6 大表单实战固化规则

来自 Round 8 与 Round 9 实测验证：

- **动态 URL 发现**：产品概览页 DOM 包含所有 6 个表单的实时 href（`a[name=princingAndAvailability]` 等），严禁在配置文件中硬编码固定 submissionId；
- **定价与可用性 (availability)**：Default 市场组选 `CNY - 中国`，价格段原生点击 `0`（¥0），发布日期选 `asap`，停止购置选 `auto-fill`；
- **属性 (properties)**：隐私区选「否，我的产品不使用任何个人信息」，单选「提供隐私策略文本」并填写文本框；产品声明勾选 storage/backups/windows，**严禁勾选 usesGenAI**；
- **年龄分级 (ageRatings)**：选择 IARC 调查表 → 应用类型选「其他所有应用类型 (value=2558)」→ 9 个追问全部选「否」→ 勾选 IARC 条款同意框并点击保存 → 点击「继续」；
- **程序包 (packages)**：上传前解包验证 MSIX 内 `AppxManifest.xml` 的 `DisplayName` 与预留名一致；**设备系列必须且仅保留 Windows 10/11 Desktop**（严禁勾选 Mobile/Xbox/Team/MixedReality）；
- **Store 一览 (listing)**：进入中文 zh-cn 页面，说明文本必填，至少 1 张 1080P 桌面运行截图，1:1 Logo (300x300)，关键词最多 7 个；
- **提交选项 (options)**：默认 `Manual` 发布模式；**页面中若出现 `runFullTrust` 用途说明文本框，必须填报 500 字以内合规用途说明**。
- **权限说明区分**：程序包页面的 `runFullTrust` 警告属于桌面应用正常提示，无需操作；提交选项页面出现的 `runFullTrust` 文本框则是必填项，两者不可混淆。
