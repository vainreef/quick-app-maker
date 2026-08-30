# Windows Smoke Test

在全新 Windows 工作目录记录每条命令、耗时、退出码、产物和版本。空目录首次入场先按仓库 README 第 0 步下载 `.qam-entry.ps1`；以下命令从工作区根目录执行。

## 1. Bootstrap

```powershell
$entry = if (Test-Path .\.qam-entry.ps1) { '.\.qam-entry.ps1' } elseif (Test-Path .\quick-app-maker\bootstrap\entry.ps1) { '.\quick-app-maker\bootstrap\entry.ps1' } elseif (Test-Path .\bootstrap\entry.ps1) { '.\bootstrap\entry.ps1' } else { throw 'Download .qam-entry.ps1 using README step 0 first.' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
$repo = if (Test-Path .\quick-app-maker\bin\qam.mjs) { '.\quick-app-maker' } else { '.' }
& "$repo\bootstrap\qam.cmd" doctor
& "$repo\bootstrap\qam.cmd" bootstrap
& "$repo\bootstrap\qam.cmd" self-test
```

重复一次，确认 Node、npm、Electron cache 和 workspace 依赖均复用。

## 2. App

```powershell
& "$repo\bootstrap\qam.cmd" create --name SmokeApp --slug smoke-app
& "$repo\bootstrap\qam.cmd" test .\smoke-app
& "$repo\bootstrap\qam.cmd" dev .\smoke-app
```

验证启动、核心操作、保存、关闭重开、空状态和错误状态。修改 renderer/main 文件，确认无需 build 即刷新或重启。

## 3. Store package

```powershell
& "$repo\bootstrap\qam.cmd" package .\smoke-app --profile store
& "$repo\bootstrap\qam.cmd" store preflight --app .\smoke-app
```

检查 MSIX、manifest identity、Executable、Assets、zh-CN、x64 和 SHA-256。

## 4. Partner Center

```powershell
& "$repo\bootstrap\qam.cmd" store launch --app .\smoke-app
& "$repo\bootstrap\qam.cmd" store reserve --app .\smoke-app --name SmokeApp
& "$repo\bootstrap\qam.cmd" store discover --app .\smoke-app
& "$repo\bootstrap\qam.cmd" store run --app .\smoke-app --apply --deadline 3600000
& "$repo\bootstrap\qam.cmd" store verify --app .\smoke-app
```

使用测试账号或真实个人开发者账号。所有阶段检查 cold Diff、Overview 模块和证据目录。最终认证提交由用户在浏览器完成。
