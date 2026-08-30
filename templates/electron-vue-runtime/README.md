# __APP_NAME__

## 当前状态

初版 Electron/Vue Runtime 模板已生成。开发链不使用 bundler，源码直接运行。

## 验证门槛

源码正则和进程列表只算静态/启动证据。交付试用前必须记录真实窗口的添加、错误、保存、关闭重开和 UI 交互证据到 `build/run-report.md`。

## 命令

从包含 `node/` 与 `quick-app-maker/` 的工作区根目录执行：

```powershell
.\quick-app-maker\bootstrap\qam.cmd dev .\__SLUG__
.\quick-app-maker\bootstrap\qam.cmd test .\__SLUG__
.\quick-app-maker\bootstrap\qam.cmd package .\__SLUG__ --profile store
.\quick-app-maker\bootstrap\qam.cmd store preflight --app .\__SLUG__
```

## 需求和商店素材

- 需求：本文件补充用户目标、核心流程和验收标准；
- 文案：`store/listing.zh-CN.md`；
- 目标状态：`store/desired-state.json`；
- 图标和截图：`store/assets/`；
- 运行记录：`build/run-report.md`。
