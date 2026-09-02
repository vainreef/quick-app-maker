# V2 命令速查手册

## 0. 命令入口与跨平台前缀解析

- **Windows 环境（PowerShell）**：
  ```powershell
  $qamRoot = if (Test-Path .\quick-app-maker\bootstrap\qam.cmd) { '.\quick-app-maker' } else { '.' }
  # 统一通过 qam.cmd 运行，内置锁定便携 Node 24 与 npm
  & "$qamRoot\bootstrap\qam.cmd" <command>
  ```
- **macOS / Linux 环境（终端）**：
  ```bash
  # 直接通过 Node 运行统一分发入口
  node bin/qam.mjs <command>
  ```

---

## 1. 环境准备与诊断

```powershell
# Windows
& "$qamRoot\bootstrap\qam.cmd" doctor     # 诊断便携 Node 24、Git、npm、Electron 镜像与沙箱健康度
& "$qamRoot\bootstrap\qam.cmd" bootstrap  # 初始化工作区依赖与 Electron 运行时
& "$qamRoot\bootstrap\qam.cmd" self-test   # 运行引擎全量契约测试

# macOS / Linux
node bin/qam.mjs doctor
node bin/qam.mjs bootstrap
node bin/qam.mjs self-test
```

---

## 2. 应用开发与质量验收

```powershell
# 1. 创建应用骨架（英文 slug 由 Agent 内部自动推导）
& "$qamRoot\bootstrap\qam.cmd" create --name "应用名称" --slug app-slug

# 2. 自动化契约质量验收（Agent 必跑，秒级退出，提供确凿证据）
& "$qamRoot\bootstrap\qam.cmd" test .\app-slug

# 3. 在后台启动开发热重载（长驻 Watcher 进程，直接向用户展示窗口）
& "$qamRoot\bootstrap\qam.cmd" dev .\app-slug
```

---

## 3. Microsoft Store 自动化发布（五步标准流程）

```powershell
# 第 1 步：启动独立 Edge 引导用户登录 Partner Center 并保留名称（秒级返回）
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
# -> 用户在 Edge 中登录并亲自点击「保留产品名称」后，在聊天框回复「我保留好了」

# 第 2 步：盘点全部素材规格，与用户确认素材来源方案（全自动生成 / 用户提供）

# 第 3 步：真机渲染高清截图与图标，整理至 store-submission-assets，置顶呼出供用户检视确认
& "$qamRoot\bootstrap\qam.cmd" screenshot .\app-slug --width 1366 --height 768 --output .\app-slug\store-submission-assets\01_应用主界面高清截图_1366x768.png
& "$qamRoot\bootstrap\qam.cmd" reveal .\app-slug\store-submission-assets
# -> 用户核对无误后，在聊天框回复「确认素材」或「继续」

# 第 4 步：自动化接力与按需精准生效
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"   # 同步 Identity
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store                  # 生产封装 MSIX
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug                    # 离线静态预检
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug                     # 发现提交草稿

# 单阶段精准直接填报（按需对未完成阶段执行直接填报）：
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase properties
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase packages
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase options

# 执行现有总体验证（确认 6 个模块均为 Complete 绿标）：
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug

# 第 5 步：提示用户在浏览器中做最后人工核对并点击「提交进行认证」
```

---

## 4. 阶段断点与排错命令

```powershell
& "$qamRoot\bootstrap\qam.cmd" store plan --app .\app-slug --phase availability   # 只读检查页面
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase <phase>       # 单独执行指定阶段直接填报
& "$qamRoot\bootstrap\qam.cmd" store status --app .\app-slug                      # 查看当前检查点与会话状态
& "$qamRoot\bootstrap\qam.cmd" store stop --app .\app-slug                        # 停止当前 Edge 会话
```
