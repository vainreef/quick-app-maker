# Edge Store CLI

这是一个**纯命令行、声明式状态收敛**的 Microsoft Edge 控制器，使用 Edge 自带的 Chromium DevTools Protocol（CDP）控制一个隔离的 Edge 进程。

它不依赖 Codex 专用浏览器工具、浏览器扩展、Selenium、Playwright 或 Node.js。Windows 自带 PowerShell 5.1，结合本仓库预备的 Edge 即可运行。

---

## 核心设计理念：声明式状态收敛

旧版命令式脚本（“navigate → setAttribute → click save”）在面对 Partner Center 的 Angular SPA 自定义控件（`he-select`, `he-option`, `he-checkbox`）与动态 Submission ID 时极易失效。

新版 CLI 升级为**声明式状态收敛系统**：
1. **Desired State**：用户在 `store-automation.json` 声明目标（免费、生产率分类、中文描述、1080P 截图等）；
2. **Observed State**：实时从产品概览页 DOM 动态嗅探当前 `submissionId` 与 6 大表单的最新 `href`；
3. **CDP 原生物理输入**：严禁硬编码绝对坐标，基于目标元素实时 `getBoundingClientRect()` 计算中心坐标，派发 CDP 原生鼠标事件；
4. **F5 刷新二次验证**：表单保存后支持强制 F5 Reload 重新读取 DOM，验证服务端真正持久化。

---

## 8 阶段标准发布流水线（Store 0 ~ Store 7）

```text
STORE 0: STATIC PREFLIGHT        # 离线解包质检 MSIX (DisplayName/Desktop-only/Logo/1080P截图/关键字<=7)
    ↓
STORE 1: SESSION DISCOVERY       # 启动/复用隔离 Edge 进程，验证 CDP 连接与用户登录
    ↓
STORE 2: LIVE COMPATIBILITY PROBE# 概览页动态读取 live submissionId 与 6 大表单实时 href
    ↓
STORE 3: PLAN                    # 对比目标配置与当前表单状态生成差异收敛计划
    ↓
STORE 4: FORM RECONCILIATION     # 逐表执行 (等待关键控件 -> CDP 物理点击填报 -> 保存)
    ↓
STORE 5: RELOAD VERIFICATION     # 刷新页面验证持久化状态无错误
    ↓
STORE 6: SUBMISSION INTEGRITY    # 概览页确认 6 大模块均显示绿色已完成状态
    ↓
STORE 7: EXPLICIT SUBMIT         # 必须显式传入 -Submit -ConfirmSubmit 触发终审
```

---

## 常用命令

### 0. 本地语法与 AST 检查
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Validate-EdgeStoreCli.ps1 -Strict
```

### 1. Store 0 离线静态质检（不打开浏览器）
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest .\<app>\build\edge-store.json
```

### 2. 启动隔离 Edge 会话
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -Manifest .\<app>\build\edge-store.json -KeepOpen
```

### 3. 只读探测与 Inspect 报告
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action inspect -Manifest .\<app>\build\edge-store.json -KeepOpen
```

### 4. 提取 Product Identity 三大核心凭据
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action identity -Manifest .\<app>\build\edge-store.json -KeepOpen
```

### 5. 单表/全表自动化填报
```powershell
# 推荐首次按表逐项执行：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase availability -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase properties   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase ageRatings   -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase packages     -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase listing      -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase options      -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 或整链一键收敛（含 F5 刷新持久化校验）：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -ReloadVerify -KeepOpen
```

### 6. 显式最终提交
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -Submit -ConfirmSubmit
```

---

## 退出码约定 (Exit Codes)

| 退出码 | 含义 |
| ---: | --- |
| 0 | 成功完成 |
| 2 | 配置文件路径或 JSON 语法错误 |
| 3 | Edge 浏览器可执行文件未找到 |
| 4 | DevTools 端口或 WebSocket 连接异常 |
| 5 | CDP 执行失败或页面 JS 抛出异常 |
| 6 | 页面或关键控件加载超时 |
| 7 | UI 结构与预设选择器不匹配 |
| 8 | 页面存在红字验证错误 (.alert-error) |
| 10 | 等待用户登录/MFA 超时 |
| 11 | Product ID 缺失或概览页无法探测到提交信息 |
| 12 | 物料配置缺失（MSIX 未找到、DisplayName 不匹配、图片不存在） |
| 13 | 最终提交前置条件未满足（存在未完成模块） |
| 14 | dry-run 模式下需要创建新提交草稿 |
