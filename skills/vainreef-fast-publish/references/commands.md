# V2 命令速查

从工作区根目录先解析引擎目录；已进入 `quick-app-maker/` 时 `$qamRoot` 为 `.`：

```powershell
# 1. 环境准备与健康检查
& "$qamRoot\bootstrap\qam.cmd" doctor
& "$qamRoot\bootstrap\qam.cmd" bootstrap
& "$qamRoot\bootstrap\qam.cmd" self-test

# 2. 生成应用脚手架
& "$qamRoot\bootstrap\qam.cmd" create --name "名称" --slug slug

# 3. 自动化测试验收（编写完 HTML/JS/CSS 业务代码后必跑，秒级退出）
& "$qamRoot\bootstrap\qam.cmd" test .\slug

# 4. 启动开发热重载（长驻服务，由用户在独立终端交互式运行体验）
& "$qamRoot\bootstrap\qam.cmd" dev .\slug
```

Store 仅在用户明确提出发布后执行：

```powershell
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\slug
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\slug --name "名称"
& "$qamRoot\bootstrap\qam.cmd" package .\slug --profile store
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\slug
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\slug
& "$qamRoot\bootstrap\qam.cmd" store run --app .\slug --apply --confirm-age-ratings --deadline 3600000
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\slug
```

阶段调试：

```powershell
& "$qamRoot\bootstrap\qam.cmd" store plan --app .\slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store status --app .\slug
& "$qamRoot\bootstrap\qam.cmd" store stop --app .\slug
```

`store plan` 为只读动作，发现差异返回 4；`store apply` 才执行写入。最终认证按钮由用户点击。进程存在只证明启动，不证明窗口已渲染；运行报告必须附动态操作或页面证据。
