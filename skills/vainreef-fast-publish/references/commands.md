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

# 3. 生成无头界面截图；其他代表性状态按验收边界补充
& "$qamRoot\bootstrap\qam.cmd" screenshot .\app-slug --width 1366 --height 768

# 4. 在后台启动开发热重载（长驻 Watcher 进程，直接向用户展示窗口）
& "$qamRoot\bootstrap\qam.cmd" dev .\app-slug
```

---

测试覆盖、截图生成与用户视觉确认分别记录，详见 [验收边界](acceptance.md)。现有命令不等于内置全部业务回归或视觉评估；补充检查使用当前已支持入口并实际验证。

## 3. Microsoft Store 自动化发布（五步标准流程）

```powershell
# 第 1 步：启动独立浏览器引导用户登录 Partner Center 并保留名称（秒级返回）
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
# -> 用户在浏览器中登录并亲自点击「保留产品名称」后，在聊天框回复「我保留好了」
# -> Agent 立即同步应用名称与回填 Identity 信息到 manifest：
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 第 2 步：盘点全部素材规格，与用户确认素材来源方案（全自动生成 / 用户提供）

# 第 3 步：无头界面渲染高清截图与图标，整理至 store-submission-assets，置顶呼出供用户检视确认
# 首页/默认视图截图：
& "$qamRoot\bootstrap\qam.cmd" screenshot .\app-slug --width 1366 --height 768 --output .\app-slug\store-submission-assets\01_应用主界面高清截图_1366x768.png
# 子页面/特定视图截图（支持 --eval 动态执行 JS 或 --click 触发按钮点击，无侵入捕获）：
& "$qamRoot\bootstrap\qam.cmd" screenshot .\app-slug --output .\app-slug\store-submission-assets\02_目录页截图_1366x768.png --eval "window.__qam_set_view?.('directory')"
& "$qamRoot\bootstrap\qam.cmd" reveal .\app-slug\store-submission-assets
# -> 用户核对无误后，在聊天框回复「确认素材」或「继续」

# 第 4 步：自动化接力与按需精准生效
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store                  # 生产封装 MSIX（64KB 块对齐）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug                    # 离线静态预检
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug                     # 发现/生成提交草稿

# 单阶段精准直接填报（按需对未完成阶段执行直接填报）：
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase properties
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase packages
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase options

# 执行现有总体验证（确认 6 个模块均为 Complete 绿标）：
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug

# macOS / Linux 对应指令：
# node bin/qam.mjs store launch --app /path/to/app-slug
# node bin/qam.mjs store reserve --app /path/to/app-slug --name "应用名称"
# node bin/qam.mjs screenshot /path/to/app-slug --width 1366 --height 768 --output /path/to/app-slug/store-submission-assets/01_应用主界面高清截图_1366x768.png
# node bin/qam.mjs reveal /path/to/app-slug/store-submission-assets
# node bin/qam.mjs package /path/to/app-slug --profile store
# node bin/qam.mjs store preflight --app /path/to/app-slug
# node bin/qam.mjs store discover --app /path/to/app-slug
# node bin/qam.mjs store apply --app /path/to/app-slug --phase <phase>
# node bin/qam.mjs store verify --app /path/to/app-slug

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

---

## 5. Windows 终端与 PowerShell 5.1 执行防御指南

在 Windows 终端（尤其是系统默认的 Windows PowerShell 5.1）与批处理包装层交互时，遵守以下客观工程规范：

1. **Windows PowerShell 5.1 语句连接**：
   - Windows PowerShell 5.1 不支持 `&&` / `||` 管道链操作符（该语法仅在 PowerShell 7+ 可用）；在 PS 5.1 环境下多条命令串行必须使用分号 `;` 分隔：
     ```powershell
     & "$qamRoot\bootstrap\qam.cmd" doctor ; & "$qamRoot\bootstrap\qam.cmd" bootstrap
     ```

2. **跨 Shell 嵌套与变量展开上下文**：
   - 从外层 Unix/bash Shell 向 PowerShell 传递命令时，若 PowerShell 源码中的 `$var` 或 `$_` 处于外层 Shell 会进行变量展开的上下文（如未加单引号或双引号字符串内），可能会被外层 Shell 提前展开置空；
   - 跨 Shell 传递时，必须使用单引号字面量保护，或对 `$` 进行正确转义（如 `\$var`），复杂逻辑优先通过脚本文件执行。

3. **PowerShell 5.1 源代码字符编码**：
   - Windows PowerShell 5.1 在读取无 BOM 脚本文件时，默认使用系统当前 ANSI 代码页（例如简体中文 Windows 系统常见为 CP936）；
   - 若生成的 `.ps1` 包含非 ASCII 字符，必须显式保存为 **UTF-8 with BOM** 编码，防止被错误解码为乱码；或在 PowerShell 7 环境下执行。

4. **多层参数转发与已知入口**：
   - 经由 PowerShell → `.cmd` → Node 多层调用时，参数中的引号与特殊符号展开规则极易受到破坏；
   - 复杂交互应优先使用 CLI **已明确支持的**结构化参数（如 `--click`）、stdin 或脚本文件入口；若工具未提供此类入口，必须严格验证多层 Quoting 转义，严禁根据未验证的推论随意发明工具能力。
