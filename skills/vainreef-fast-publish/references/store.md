# Partner Center V2 自动化发布手册

## 0. 账号、证书与浏览器常识

- **Edge 浏览器驱动机制**：自动化发布采用**宿主机内置 Microsoft Edge 独立实例**（Mac 环境自动兼容 Edge/Chrome），通过 Chrome DevTools Protocol (`connectOverCDP`) 由 Playwright 控制，使用工作区独立 profile 沙箱（`.cache/qam/session`），不读取、不影响用户日常浏览数据；
- **开发者账号**：微软账号与个人开发者认证免费，无需高昂费用；若用户未注册或未认证，运行 `store launch` 自动拉起 Edge 浏览器，指引用户在网页中完成注册或个人认证；
- **代码签名**：上架微软商店的 MSIX 包由**微软商店官方在云端自动完成安全签名**（Store Signing），**完全不需要开发者购买或提供第三方代码签名证书**，严禁向用户询问证书问题！

## 1. 微软商店发布五步人机协同标准流程

```text
第 1 步：登录与应用名称保留协同 (store launch -> 用户在 Edge 中亲自点击保留 -> store reserve 回填)
  ↓
第 2 步：上架材料全面盘点与来源协同 (向用户列出文案、图片规格，确认由 Agent 生成还是用户提供)
  ↓
第 3 步：真机素材生成、交互式弹窗与用户检视确认 (纯中文命名、纯文本 txt、置顶展示并等待用户回复确认)
  ↓
第 4 步：按需精准生效与全自动流水线 (优先单阶段 store apply --phase，最后 store verify 一键总验)
  ↓
第 5 步：最终人工核对与提交 (用户在已打开的浏览器中复核并亲自点击「提交进行认证」)
```

## 2. 自动化流水线标准指令（严禁跳步或颠倒时序）

```powershell
# 1. 启动独立 Edge 引导用户登录/注册 Partner Center (秒级返回)
& "$qamRoot\bootstrap\qam.cmd" store launch --app .\app-slug
# -> 用户在 Edge 中登录并亲自保留名称后，在聊天框回复「我保留好了」

# 2. 自动化同步应用名称与回填 Identity 信息到 manifest
& "$qamRoot\bootstrap\qam.cmd" store reserve --app .\app-slug --name "应用名称"

# 3. 生产封装 MSIX（必须在 reserve 之后执行）
& "$qamRoot\bootstrap\qam.cmd" package .\app-slug --profile store

# 4. 离线静态预检（校验 MSIX、manifest、素材尺寸与文案）
& "$qamRoot\bootstrap\qam.cmd" store preflight --app .\app-slug

# 5. 发现当前提交草稿路由
& "$qamRoot\bootstrap\qam.cmd" store discover --app .\app-slug

# 6. 单阶段精准直接填报 (Direct-Apply，按需填报未完成模块)
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase availability
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase properties
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase age-ratings --confirm-age-ratings
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase packages
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase listing
& "$qamRoot\bootstrap\qam.cmd" store apply --app .\app-slug --phase options

# 7. 冷加载总检验证（确认 6 个模块均为 Complete 绿标）
& "$qamRoot\bootstrap\qam.cmd" store verify --app .\app-slug
```

## 3. 页面自动化操作与收敛判定

- **Playwright 定位器**：首选 `getByRole`、`getByLabel`、`getByText`，默认穿透 open Shadow DOM；
- **Web Components 支持**：`<he-select>` 内部直接键入回车生效，`<he-button>` 支持复合定位；
- **大包上传支持**：50MB+ 大文件通过 CDP `DOM.setFileInputFiles` 注入；
- **收敛判定链**：`PageKind → Observe → Apply (直接填表并保存成功) → Overview Complete → Converged`；
- **人工终审边界**：`store verify` 全绿标后，CLI 不会自动点击最终的“提交进行认证”按钮，留给用户在浏览器中复核并点击。
