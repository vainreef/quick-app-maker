# V2 架构

```text
portable Node → qam CLI → generator → Electron App
                         ├→ node:test / Electron E2E
                         ├→ production layout → WinApp CLI → MSIX
                         └→ Playwright Edge → Partner Center
```

## 默认 App

Electron production packager、JavaScript、Vue browser runtime、secure preload。源码直接执行；watcher 只做 reload/restart。

## Store 自动化

`packages/store-core` 管 Desired、Observed、Diff、Checkpoint、证据和时间预算；`packages/store-playwright` 管 Edge session、PageKind、Overview、六个阶段和脱敏 fixtures；`packages/store-preflight` 管 MSIX/manifest/PNG/文案静态检查。

## 设计边界

- 业务逻辑全部 Node；
- PowerShell 只做 Node 初始下载和可见桌面桥；
- 默认依赖无 native rebuild；
- 远程内容没有 Node 权限；
- final certification click 不在 CLI 内。
