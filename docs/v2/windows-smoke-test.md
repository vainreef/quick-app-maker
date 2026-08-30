# Windows Smoke Test

在全新 Windows 工作目录记录每条命令、耗时、退出码、产物和版本。Edge、PowerShell 作为 Windows 内置能力处理；Node 和 Git 只看工作区便携副本。

## 1. Bootstrap

```powershell
$entry = if (Test-Path .\.qam-entry.ps1) { '.\.qam-entry.ps1' } elseif (Test-Path .\quick-app-maker\bootstrap\entry.ps1) { '.\quick-app-maker\bootstrap\entry.ps1' } elseif (Test-Path .\bootstrap\entry.ps1) { '.\bootstrap\entry.ps1' } else { throw '先按 README 第 0 步下载 .qam-entry.ps1。' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
$qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
& "$qamRoot\bootstrap\qam.cmd" doctor
& "$qamRoot\bootstrap\qam.cmd" bootstrap
& "$qamRoot\bootstrap\qam.cmd" self-test
```

重复执行一次，确认 Node、npm、Electron cache 和 workspace 依赖复用；工作区根目录没有 `qam-toolchain.lock.json` 也应能通过。

## 2. App

```powershell
& "$qamRoot\bootstrap\qam.cmd" create --name SmokeApp --slug smoke-app
& "$qamRoot\bootstrap\qam.cmd" test .\smoke-app
```

在单独终端启动 `& "$qamRoot\bootstrap\qam.cmd" dev .\smoke-app`，关闭时回到该终端使用 Ctrl+C。自动化启动时记录返回的会话 PID，只结束该 PID 树，不按名称批量结束 Electron 或 Node。

验证空状态、添加/编辑/删除、空输入、非法日期、未来/过去/今天、保存失败、关闭重开和庆祝层关闭路径。修改 renderer 确认刷新；修改 main/preload 确认重启。DevTools 只有显式设置 `$env:QAM_DEVTOOLS='1'` 时才打开。

## 3. Store package

```powershell
& "$qamRoot\bootstrap\qam.cmd" package .\smoke-app --profile store
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\smoke-app
```

检查 MSIX、manifest Identity、Executable、Assets、zh-CN、x64 和 SHA-256；通用占位文案和占位截图不计入通过。

## 4. Partner Center

```powershell
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\smoke-app
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\smoke-app --name SmokeApp
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\smoke-app
& "$qamRoot\bootstrap\qam.cmd" store run --app .\smoke-app --apply --deadline 3600000
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\smoke-app
```

所有阶段检查 cold Diff、Overview 模块和证据目录。最终认证提交由用户在浏览器完成。若当前机器不是 Windows，报告静态检查结果并把动态项标记为待 Windows 验证，不把进程列表当作 UI 证据。
