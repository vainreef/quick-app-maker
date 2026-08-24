# Edge Store CLI

这是一个**纯命令行**的 Microsoft Edge 控制器，使用 Edge 自带的 Chromium DevTools Protocol（CDP）控制一个隔离的 Edge 进程。

它不依赖 Codex 专用浏览器工具、浏览器扩展、Selenium、Playwright 或 Node.js。Windows 自带 PowerShell 5.1，加上已经由本仓库准备的 Edge 浏览器即可运行。

## 目标

自动完成 Partner Center 的商店提交草稿：

1. 进入产品提交页；
2. 定价和可用性；
3. 属性；
4. 年龄分级；
5. 程序包上传与设备系列；
6. 中文 Store 一览；
7. 提交选项；
8. 检查 6 个模块后，再执行最终提交。

## 为什么使用隔离 Edge

- 默认创建专用 `state/edge-profile`，不碰用户日常 Edge 配置；
- 用户首次运行时自行登录、完成 MFA 或 CAPTCHA；脚本不读取密码、Cookie、Local Storage 或浏览器凭据；
- 脚本只记录产品 ID、提交 ID、阶段和页面标题，不保存页面全文；
- 只结束脚本自己启动的 Edge PID；
- 保存后立即读取页面错误区，发现校验异常就停止；
- 选择器匹配 0 个或多个元素时停止并生成 inspect 报告，不进行猜测点击；
- 默认选择 `Manual` 发布模式，避免认证通过后自动发布；
- 最终「提交到应用商店」需要同时传入 `-Submit -ConfirmSubmit`。

## 首次启动

先做本地语法和 JSON 检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Validate-EdgeStoreCli.ps1
```

Qiangua 首轮使用严格模式：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Validate-EdgeStoreCli.ps1 `
  -Manifest .\apps\Project-02\qiangua\build\edge-store.json -Strict
```

在 Windows PowerShell 中执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action launch `
  -Manifest .\toolchain\edge-store-cli\examples\store-automation.json `
  -KeepOpen
```

Edge 会以专用 profile 打开 Partner Center。用户在 Edge 窗口中完成登录和 MFA 后，保留窗口即可。

## Qiangua 当前流程

`apps/Project-02` 的过程记录中已经确认：

- Product ID：`9N8596K7D41F`
- Submission ID：`1152921505701723776`
- 中文语言 ID：`5`
- 中文语言代码：`zh-cn`
- 商店包：`Vainreef.440063905AF20_1.0.3.0_x64.msix`
- 定价：`CNY - 中国` + 价格段 `0`（¥0）
- 隐私策略：直接填写「提供隐私策略文本」

本地配置文件：

```text
apps/Project-02/qiangua/build/edge-store.json
```

先运行只读检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action inspect `
  -Manifest .\apps\Project-02\qiangua\build\edge-store.json `
  -KeepOpen
```

如果需要从产品概览页提取 3 个包身份参数：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action identity `
  -Manifest .\apps\Project-02\qiangua\build\edge-store.json `
  -KeepOpen
```

结果写入 `toolchain/edge-store-cli/state/product-identity.json`。

只读检查通过后，执行完整填表和保存：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action run `
  -Manifest .\apps\Project-02\qiangua\build\edge-store.json `
  -Apply `
  -KeepOpen
```

首次 Windows 实机建议按过程记录逐表执行，每一阶段确认页面状态后再进入下一阶段：

```powershell
$cli = Join-Path (Get-Location) 'toolchain\edge-store-cli\Invoke-EdgeStore.ps1'
$cfg = Join-Path (Get-Location) 'apps\Project-02\qiangua\build\edge-store.json'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase availability -Manifest $cfg -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase properties   -Manifest $cfg -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase ageRatings   -Manifest $cfg -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase packages     -Manifest $cfg -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase listing      -Manifest $cfg -Apply -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cli -Action run -Phase options      -Manifest $cfg -Apply -KeepOpen
```

脚本会在每个阶段保存状态到：

```text
toolchain/edge-store-cli/state/store-state.json
toolchain/edge-store-cli/state/logs/edge-store.log
```

中途出错时保留 Edge 窗口和状态，修复页面或配置后重新执行同一条命令即可从已完成阶段继续。

## 最终提交

默认流程只填写并保存草稿，不点击最终提交。确认所有模块在概览页显示完成后，再显式执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action run `
  -Manifest .\apps\Project-02\qiangua\build\edge-store.json `
  -Apply -Submit -ConfirmSubmit
```

`Manual` 发布模式仍然保持生效；认证通过后不会自动发布，后续可在 Partner Center 中选择立即发布。

## CLI 行为约定

| 退出码 | 含义 |
| ---: | --- |
| 0 | 阶段完成 |
| 2 | 配置或命令参数问题 |
| 3 | Edge 路径问题 |
| 4 | DevTools 端口或 WebSocket 连接问题 |
| 6 | 页面加载超时 |
| 7 | 页面结构与已验证选择器不一致 |
| 8 | 页面存在验证错误 |
| 10 | 等待用户登录/MFA 超时 |
| 11 | Product ID 或 Submission ID 缺失 |
| 12 | 文件或物料配置问题 |
| 13 | 最终提交前置条件未满足 |
| 14 | dry-run 下需要创建新提交 |

## 页面结构依据

选择器来自 `apps/Project-02/process.md` 与 Partner Center DOM 快照，优先使用：

- `data-l10n-key`
- `data-automation-id`
- `id`
- `name`
- `uitestid`
- `aria-labelledby`

页面结构发生变化时，先执行 `-Action inspect`，检查生成的 `state/inspect-*.json`，再更新脚本中的选择器。脚本会停在变化处，避免把操作误点到其他表单。

## Edge 版本与远程调试

脚本使用本机回环地址 `127.0.0.1` 和临时端口启动 Edge：

```text
--user-data-dir=<isolated profile>
--remote-debugging-port=<free local port>
--remote-debugging-address=127.0.0.1
```

远程调试只绑定本机，专用 profile 与用户日常 profile 分离。企业策略若关闭 Edge DevTools，脚本会返回明确的连接错误，不修改策略、不写注册表。

Microsoft 官方资料：

- [Use WebDriver to automate Microsoft Edge](https://learn.microsoft.com/en-us/microsoft-edge/webdriver/)
- [RemoteDebuggingAllowed policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/remotedebuggingallowed)
- [Microsoft Store submission API](https://learn.microsoft.com/en-us/windows/apps/publish/store-submission-api)
