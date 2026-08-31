# Partner Center V2 自动化发布手册

## 0. 账号与证书常识（避免误导用户）

- **开发者账号**：微软账号与个人开发者认证免费，无需高昂费用；若用户未注册或未认证，运行 `store launch` 自动拉起 Edge 浏览器，指引用户在网页中完成注册或个人认证；
- **代码签名**：上架微软商店的 MSIX 包由**微软商店官方在云端自动完成安全签名**（Store Signing），**完全不需要开发者购买或提供第三方代码签名证书**，严禁向用户询问证书问题！

## 1. 自动化流水线标准时序（严禁跳步或颠倒时序）

```powershell
# 1. 启动独立 Edge 引导用户登录/注册 Partner Center
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug

# 2. 自动化保留应用名称，自动回填 ProductId 与 Identity 到 manifest
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 3. 生产封装 MSIX（必须在 reserve 之后执行）
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store

# 4. 离线静态预检（校验 MSIX、manifest、素材尺寸与文案）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug

# 5. 发现当前提交草稿路由
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

# 6. 一键自动化填写六大阶段（定价、属性、年龄分级问卷、程序包上传、商店文案与选项）
& "$qamRoot\bootstrap\qam.cmd" store run --app .\app-slug --apply --confirm-age-ratings --deadline 3600000

# 7. 冷加载总检验证（确认 6 个模块均为 Complete 绿标）
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug
```

## 2. 页面自动化操作与收敛判定

- **Playwright 定位器**：首选 `getByRole`、`getByLabel`、`getByText`，默认穿透 open Shadow DOM；
- **收敛判定链**：`PageKind → Observe → Diff → Apply → 冷加载 → Diff=0 → Overview Complete → Converged`；
- **人工终审边界**：`store verify` 全绿标后，CLI 不会自动点击最终的“提交进行认证”按钮，留给用户在浏览器中复核并点击。
