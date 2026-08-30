# V2 Bootstrap

`entry.ps1` 是 Node 尚未存在时的最小下载桥：下载 portable Node、获取仓库、转交 `node bin/qam.mjs bootstrap`。工具链检测、npm cache、Electron mirror、依赖安装和报告由 Node 实现。

所有缓存位于当前工作区 `.cache/`，Node 位于当前工作区 `node/`，Git 位于当前工作区 `git/`。没有全局 Node、全局 npmrc 或系统安装步骤。
