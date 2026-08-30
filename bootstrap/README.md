# V2 Bootstrap

`entry.ps1` 是 Node 尚未存在时的最小下载桥：它会在当前工作区下载并校验便携 Node 与 MinGit，克隆或更新仓库，使用工作区内置 npm 安装依赖，然后启动 `qam.mjs bootstrap` 完成 Electron 运行时准备。

## 首次启动

空目录请先按根目录 README 第 0 步下载 `.qam-entry.ps1`。仓库已经位于当前目录时，在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\bootstrap\entry.ps1
```

入口脚本始终使用：

- `WORKSPACE_ROOT\node\node.exe`
- `WORKSPACE_ROOT\node\node_modules\npm\bin\npm-cli.js`
- `WORKSPACE_ROOT\git\cmd\git.exe`
- `WORKSPACE_ROOT\.cache\npm`
- `WORKSPACE_ROOT\.cache\electron`

Git 不读取系统 PATH；MinGit 缺失时下载 ZIP、校验 SHA-256、验证 `cmd\git.exe` 后再投入使用。

## 后续命令

从包含 `node/` 与 `quick-app-maker/` 的工作区根目录执行：

```powershell
.\quick-app-maker\bootstrap\qam.cmd doctor
.\quick-app-maker\bootstrap\qam.cmd create --name "应用名称" --slug app-slug
.\quick-app-maker\bootstrap\qam.cmd dev .\app-slug
.\quick-app-maker\bootstrap\qam.cmd test .\app-slug
.\quick-app-maker\bootstrap\qam.cmd self-test
```

`qam.cmd` 会检查便携 Node 路径并设置 `QAM_WORKSPACE_ROOT`，同时在依赖缺失时先执行一次 workspace `npm ci`，避免后续命令落到系统 Node/npm。

工具链 lock 位于 `quick-app-maker/qam-toolchain.lock.json`，由引擎命令显式加载。首次 bootstrap 不需要把 lock 手工复制到工作区根目录；在空工作区直接执行 `create` 也应通过。

如果仓库本身就在工作区根目录，命令前缀使用 `.\bootstrap\qam.cmd`。

## 发布入口同步

全新空目录的第一步会从 Gitee raw 地址下载 `bootstrap/entry.ps1`，因此发布新版本时必须同步更新仓库内脚本和远端 raw 脚本；否则首启会运行旧版本。
