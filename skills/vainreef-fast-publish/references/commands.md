# V2 命令速查

工作区执行入口前缀解析：
```powershell
$qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
```

## 1. 环境准备与诊断
```powershell
& "$qamRoot\bootstrap\qam.cmd" doctor     # 诊断便携 Node 24、Git、npm、Electron 镜像与沙箱健康度
& "$qamRoot\bootstrap\qam.cmd" bootstrap  # 初始化工作区依赖与 Electron 运行时
& "$qamRoot\bootstrap\qam.cmd" self-test   # 运行引擎 34 项全量契约测试
```

## 2. 应用开发与质量验收
```powershell
& "$qamRoot\bootstrap\qam.cmd" create --name "应用名称" --slug app-slug  # 创建应用骨架
& "$qamRoot\bootstrap\qam.cmd" test .\app-slug                           # 自动化质量验收（秒级退出，提供确凿证据）
& "$qamRoot\bootstrap\qam.cmd" dev .\app-slug                            # 启动开发热重载（长驻 Watcher，供用户体验）
```

## 3. Microsoft Store 自动化发布（用户确认后触发）
```powershell
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug             # 启动隔离 Edge 会话引导登录
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称" # 保留名称并回填 Identity
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store        # 生产封装生成 Store MSIX 包
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug          # 离线静态预检（校验 manifest/素材/文案）
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug           # 发现或创建本次提交草稿
& "$qamRoot\bootstrap\qam.cmd" store run --app .\app-slug --apply --confirm-age-ratings --deadline 3600000 # 自动化填写六大阶段
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug            # 冷加载总体验证（确认 6 模块均为 Complete 绿标）
```

## 4. 阶段断点与排错
```powershell
& "$qamRoot\bootstrap\qam.cmd" store plan --app .\app-slug --phase availability   # 只读检查差异（有差异退出码 4）
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability  # 单独执行指定阶段填报
& "$qamRoot\bootstrap\qam.cmd" store status --app .\app-slug                      # 查看当前检查点与会话状态
& "$qamRoot\bootstrap\qam.cmd" store stop --app .\app-slug                        # 停止当前 Edge 会话
```

> **重要边界**：`store verify` 通过后，CLI 不会自动点击最终的“提交进行认证”按钮，由用户在浏览器中亲自复核并点击提交。
