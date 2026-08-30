# 国内网络与缓存

- npm registry：`https://registry.npmmirror.com`；
- Electron mirror：`https://npmmirror.com/mirrors/electron/`；
- Node portable 与 MinGit：只使用 `qam-toolchain.lock.json` 中的 URL、版本和 SHA-256；
- Git 路径固定为 `WORKSPACE_ROOT/git/cmd/git.exe`；不探测或回退到系统 Git；
- 下载桥使用 PowerShell 内置 `Invoke-WebRequest`，不依赖系统 `curl.exe`；
- cache：`WORKSPACE_ROOT/.cache/npm`、`.cache/electron`、`.cache/downloads`；
- 下载使用 `.part`、SHA-256、原子改名和有限重试；
- `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`；Playwright 只连接独立 Edge，不下载第二份浏览器。
