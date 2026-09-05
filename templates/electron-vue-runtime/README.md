# __APP_NAME__

## 当前状态

初版 Electron/Vue Runtime 模板已生成。开发链不使用 bundler，源码直接运行。

## 验证与试用

1. 编写业务代码后，通过 `qam test` 完成语法、模板与 IPC 契约自动化验收；
2. 通过 `qam screenshot` 捕获无头真机截图进行视觉防呆；
3. 后台启动 `qam dev` 直接展示应用窗口，由用户亲自在真实窗口中体验交互、输入与数据持久化。

## 命令

从包含 `node/` 与 `quick-app-maker/` 的工作区根目录执行：

```powershell
.\quick-app-maker\bootstrap\qam.cmd test .\__SLUG__                          # 自动化测试验收（秒级退出）
.\quick-app-maker\bootstrap\qam.cmd dev .\__SLUG__                           # 启动开发热重载（长驻服务，供用户试用）
.\quick-app-maker\bootstrap\qam.cmd package .\__SLUG__ --profile store       # 生产封装生成 Store MSIX 包
.\quick-app-maker\bootstrap\qam.cmd store preflight --app .\__SLUG__         # 离线静态预检
```

## 需求和商店素材

- 需求：本文件补充用户目标、核心流程和验收标准；
- 文案：`store/listing.zh-CN.md`；
- 目标状态：`store/desired-state.json`；
- 图标和截图：`store/assets/`。
