# Windows Smoke Test

在全新 Windows 工作目录记录每条命令、耗时、退出码、产物和版本。

## 1. Bootstrap

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\bootstrap\entry.ps1
node .\bin\qam.mjs doctor
node .\bin\qam.mjs bootstrap
```

重复一次，确认 Node、npm、Electron cache 和 workspace 依赖均复用。

## 2. App

```powershell
node .\bin\qam.mjs create --name SmokeApp --slug smoke-app
node .\bin\qam.mjs test .\smoke-app
node .\bin\qam.mjs dev .\smoke-app
```

验证启动、核心操作、保存、关闭重开、空状态和错误状态。修改 renderer/main 文件，确认无需 build 即刷新或重启。

## 3. Store package

```powershell
node .\bin\qam.mjs package .\smoke-app --profile store
node .\bin\qam.mjs store preflight --app .\smoke-app
```

检查 MSIX、manifest identity、Executable、Assets、zh-CN、x64 和 SHA-256。

## 4. Partner Center

```powershell
node .\bin\qam.mjs store launch --app .\smoke-app
node .\bin\qam.mjs store reserve --app .\smoke-app --name SmokeApp
node .\bin\qam.mjs store discover --app .\smoke-app
node .\bin\qam.mjs store run --app .\smoke-app --apply --deadline 3600000
node .\bin\qam.mjs store verify --app .\smoke-app
```

使用测试账号或真实个人开发者账号。所有阶段检查 cold Diff、Overview 模块和证据目录。最终认证提交由用户在浏览器完成。
