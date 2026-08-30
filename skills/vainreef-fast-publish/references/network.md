# 国内网络与缓存

- npm registry：`https://registry.npmmirror.com`；
- Electron mirror：`https://npmmirror.com/mirrors/electron/`；
- Node portable：锁文件中的 npmmirror URL；
- cache：`WORKSPACE_ROOT/.cache/npm`、`.cache/electron`、`.cache/downloads`；
- 下载使用 `.part`、SHA-256、原子改名和一次重试；
- `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`。
