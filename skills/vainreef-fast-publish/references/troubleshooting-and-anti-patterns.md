# Microsoft Partner Center 自动化异常诊断与自愈手册 (Troubleshooting & Anti-Patterns)

本文档系统性汇总了在 Microsoft Partner Center 商店发布流水线中遇到的真实 DOM 异构、前端异步行为及 CDP 驱动边界坑点，供 Agent 快速查阅并执行精准自愈。

---

## 1. 语言网格字符串模糊匹配 Bug (Manage Languages Substring Bug)

- **错误现象**：在 `managelanguages` 页面执行删除非目标语言时，大量外语（如阿尔巴尼亚语 `ID: 151`、卡纳达语 `ID: 52`、波斯尼亚语 `ID: 115`）被误判为目标语言而未被删除，导致页面残留 80+ 门语言。
- **根本原因**：代码中使用 `(href || '').includes('languageid=5')` 或 `includes('5')` 进行子串匹配，由于 `151`、`52`、`115`、`45`、`159` 均包含数字 `5`，导致正则误命中。
- **正确规范**：
  ```javascript
  // 必须使用严格正则定界符 (?:[?&]) ... (?:&|$)
  const isTargetLanguage = /(?:[?&])languageid=5(?:&|$)/.test(href) || /(?:[?&])languagecode=zh-cn(?:&|$)/i.test(href);
  ```
- **工作流标准**：
  1. 先调用 `DetectLanguagesAsync` 结构化提取当前所有活跃行并打印日志；
  2. 针对非目标语言逐行触发删除；
  3. 点击页面底部【保存】按钮提交语言网格变更。

---

## 2. 视觉资产 CDP 挂载缺少事件派发 (File Upload Change/Input Event Drop)

- **错误现象**：执行 `DOM.setFileInputFiles` 后，页面上的屏幕截图数量依然显示为 `桌面 (0)`，缩略图未生成，导致 Store 一览无法完成。
- **根本原因**：CDP 的 `DOM.setFileInputFiles` 只在 `<input type="file">` 节点内部设置了 `files` FileList 对象，但**不会自动触发浏览器的 DOM `change` 和 `input` 事件**。Angular 绑定的 `(change)="onFileSelected($event)"` 上传处理器未被激活，未能将文件推送到 Azure Blob Storage。
- **正确规范**：
  在 `SetFileInputFilesByObjectIdAsync` 执行完 CDP 命令后，**必须立即通过 `Runtime.callFunctionOn` 显式派发事件**：
  ```javascript
  this.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
  this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  ```
- **保存前硬校验**：
  在调用 Save 之前，必须执行 DOM 断言：
  ```csharp
  bool hasScreenshot = await SectionHasImageAsync(["屏幕截图", "screenshot", "desktop", "桌面"], -1);
  if (!hasScreenshot) throw new InvalidOperationException("[ERROR] 必须确保桌面屏幕截图上传成功才能点击保存！当前截图数量为 0。");
  ```

---

## 3. 关键词（Tag）超过 7 个上限报错与折叠面板不可见 (Keyword Capacity & Accordion Collision)

- **错误现象**：填报关键词时报错 `Cannot find single visible clickable element for [keyword control]` 或 Partner Center 提示 `关键字最多为 7 个`。
- **根本原因**：
  1. `#search-terms` 处于 `其他信息` 折叠面板内，页面默认处于折叠隐藏状态，直接点击会因为元素尺寸为 0 抛出不可交互异常；
  2. 历史提审草稿中残留了超过 7 个关键词（例如 12 个），继续添加会触发微软商店 7 词硬性红线。
- **自愈算法**：
  1. **自动展开面板**：查找包含 `其他信息`、`显示`、`展开` 的按钮并执行展开；
  2. **读取已存标签**：提取当前页面上所有 `<he-option>` 的内容；
  3. **清理多余旧标签**：定位非目标标签 Chip 上的删除/关闭图标（`[aria-label*="删除"]` 或 `he-icon[name="cancel"]`）并逐一点击移除；
  4. **受控追加新词**：仅在剩余配额允许的情况下向 `<he-select>` 输入新词，严格确保最终关键词总数 $\le 7$ 个。

---

## 4. 提交选项受限功能异步请求延迟 (Restricted Capabilities Asynchronous Delay)

- **错误现象**：在 `options` 页面找不到 `runFullTrust` 理由输入框。
- **根本原因**：`options` 页面的基础骨架渲染非常快，但 `<section>受限的功能</section>` 是 Angular 前端在异步拉取后台包能力分析接口后动态插入 DOM 的。如果页面刚加载就立刻查找，会因为异步接口未返回而查空。
- **正确规范**：
  必须执行两阶段流程并加入针对动态 section 的显式等待：
  ```csharp
  await waiter.RequireAsync(async () =>
  {
      return await client.EvaluateAsync<bool>("document.querySelector('textarea.text-area-width, [dcl10n=\"resCapSectionHeader\"]') !== null || (document.body?.innerText || '').includes('runFullTrust')");
  }, TimeSpan.FromSeconds(30), "Wait for restricted capabilities section");
  ```

---

## 5. 全局隐私政策统一规范 (Global Privacy Policy Invariant)

- **规范**：`quick-app-maker` 生态下的所有 Windows App 均属于本地轻量运行工具，在 Partner Center 属性（Properties）板块中：
  - **必须选择**：`是，包含隐私策略`（Privacy: Yes）；
  - **必须填入**：`本应用为本地运行工具，不收集、不存储、不上传任何用户个人隐私数据或使用习惯。`

---

## 6. 产品预留名称自动建项与名称占用排障 (Product Name Reservation Protocol)

- **错误现象**：在预留名称阶段，页面出现红字警告：`名称不可用。如果已保留此名称用于其他产品，请将其更新为不使用此名称。`
- **根本原因**：微软商店中的产品名称具有全球唯一性。常用词、单字词（如“牵挂”、“记事本”）通常已被其他开发者占用或受微软官方保留。
- **重构后的标准工作流**：
  1. Agent 使用用户已确认的名称执行 `Invoke-EdgeStore.ps1 -Action reserve -AppName "<AppName>" -Manifest <manifest>`；
  2. 驱动自动检查可用性、预留名称、进入产品页、提取 ProductId 与 3 大 Identity，并回填 manifest；
  3. 名称占用时保留微软原始错误与退出码，回到产品命名决策，不向用户输出 Partner Center 手工点击步骤；
  4. 用户确认新名称后再执行一次 reserve，避免脚本自行改变公开品牌名。

---

## 7. MSIX 清单 DisplayName 与商店预留名称不匹配拒包 (Package DisplayName Matching Invariant)

- **错误现象**：在 Packages 页面上传 MSIX 程序包后，微软后台返回红色错误提示：
  > `此软件包的清单（Package/Properties/DisplayName）使用了你未保留的显示名称: xxx`
  > `Delete the faulty package(s) listed above before you can save your changes.`
- **根本原因**：
  - 微软 Partner Center 要求 MSIX 的 `Package.appxmanifest` 中的 `<Properties><DisplayName>` 和 `<Application ... uap:VisualElements DisplayName="...">`，**必须与微软后台为该产品已保留的名称列表 100% 完全一致**；
  - 若在后台预留了 `Qiangua - 牵挂桌面记事与倒数日`，但本地打包清单中只写了 `牵挂`，微软校验器会判定该包使用了“未保留名称”而报错拒包。
- **正确自愈与闭环规范**：
  1. 在 STORE -1 用户预留名称成功后，脚本从页面抓取到实际预留的产品名称（`actualProductName`）；
  2. 自动将 `actualProductName` 同步更新到 `edge-store.json` 的 `productName` 以及 `Package.appxmanifest` 中的所有 `DisplayName` 节点；
  3. 按 `references/toolchain/v1/commands.md` 的自包含 Store 路线执行一次 `dotnet publish` 与 `winapp package`。

---

## 8. Packages 上传验证负反馈实时拦截与异常包自愈 (Packages Realtime Error Trap & Auto-Clean)

- **错误现象**：MSIX 上传出错后，页面已显示红色错误提示，但旧版脚本依然在单向轮询 `Validated` 状态直到 12 分钟超时。
- **根本原因**：轮询循环缺乏多态负反馈拦截，且旧版文本匹配逻辑中 `rowText.includes('mb')` 或 `desktop` 误命中了错误提示行，导致死等。
- **自愈机制**：
  1. **实时负反馈拦截器**：每 500ms 扫描 `.alert-error`、`.alert-danger`、`.faulty-package-message`，一旦微软返回校验失败，秒级捕获具体错误并抛出异常中断；
  2. **自动清理异常包（Auto-clean Faulty Packages）**：在重新上传前，自动扫描并点击页面上的 `a.upload-action[data-l10n-key="app_package_action_delete"]` / `Delete` 按钮，清理掉所有残留的损坏包，恢复纯净上传环境。

---

## 9. 绝对禁止机器硬编码检查成功与报错 (Agent-Driven DOM Verification Invariant)

- **错误现象**：网页上某模块（如属性）实际上已经成功保存并渲染出绿色的 `<he-badge class="text-green">完成</he-badge>`，但 C# 驱动在 `AssertComplete` 中全局匹配链接时误命中了左侧导航栏的菜单链接，导致驱动抛出 `Overview verification rejected phase [properties]: module status is Incomplete` 异常中断流程。
- **根本原因**：机器硬编码的 CSS 选择器在复杂 Angular SPA 中极易受到侧边栏、面包屑或隐藏元素的污染，不能代替 Agent 对现场真实 DOM 的综合审查。
- **正确规范与自愈**：
  1. **驱动输出结构化证据**：CLI 在各板块保存后提取提审卡片（`#collapseSubmissionSetup` / `.accordion-body` / `he-card`）及模块状态；
  2. **Agent 审查确权**：Agent 根据现场 DOM、错误区域、冷加载回读和概览模块状态裁决；
  3. **完成语义**：只有完整阶段 Diff 为零、冷加载回读仍为零且对应概览模块为 `Complete` 时，checkpoint 才进入 `Converged`。选择器歧义、`Unknown`、`Processing` 和错误行都保持未完成。

---

## 10. 提审草稿概览 URL 重定向与死循环探测排障 (Submission URL Router & Discovery Loop)

- **错误现象**：在已有活跃提交草稿的情况下，脚本导航到 `.../submissions/{id}/overview` 会被微软后台前端路由自动重定向为 `.../overview`，导致旧版脚本超时误判为“无活跃提交”而跳转到总概览寻找不存在的【开始提交】按钮，最终抛出 `Failed to discover submission links or create new submission draft` 异常。
- **根本原因**：微软 Partner Center 对当前唯一草稿的 URL 进行了重写；
- **正确规范**：一旦已知 `submissionId`（存在于 `checkpoint.json` 或 `edge-store.json` 中），直接使用确定的 6 大模块直达 URL（`${root}/availability`、`${root}/properties` 等）进行导航，杜绝任何多余的概览盲等和备用草稿创建探测。

---

## 11. MSIX 资源语言声明与 Store Listing 语言联动机制 (MSIX Resource Languages & Listing Languages)

- **错误现象**：在 `managelanguages` 页面中只显示 `英语(美国)`，没有 `中文(中国)`，导致脚本硬编码跳转 `languageid=5&languagecode=zh-cn` 时出现空白或等待超时。
- **根本原因**：`Package.appxmanifest` 中配置的是 `<Resource Language="x-generate"/>`，缺少独立资源字典目录时默认被打包为单语言 `en-US`；微软后台扫描后仅创建了默认的 `英语(美国)` 槽位。
- **正确规范**：
  1. 在 `Package.appxmanifest` 中显式声明 `<Resource Language="zh-CN"/>`，确保打包出的 PRI 资源主语言与目标语言一致；
  2. `ListingAdapter` 详情页跳转严禁写死 `languageid=5`，必须动态读取当前表格中实际存在的语言槽位行（`[slot^="Name-"] a` 或 `a[href*="/listings?languageid="]`）进行点击进入。

---

## 12. .NET Host 进程文件句柄占用与干净编译 (DLL Locking & Process Cleanliness)

- **错误现象**：修改 C# 代码后执行 `dotnet build` 提示 `error MSB3027: Could not copy obj to bin... The file is locked by: .NET Host`。
- **根本原因**：后台运行的 CLI 进程尚未完全释放程序集句柄。
- **正确规范**：
  1. 停止本轮启动记录中的精确 CLI/App PID；
  2. 执行 `dotnet build-server shutdown` 释放 MSBuild/VBCSCompiler 节点；
  3. 确认没有仍在写同一 `bin/obj` 的命令后，再按需清理；
  4. 使用前台 `dotnet build ... /p:UseSharedCompilation=false` 重试一次；
  5. 避免按进程名批量结束机器上的全部 `dotnet`，以免影响其他任务。

---

## 13. 提审选项受限功能理由输入与全阴影 DOM 穿透保存 (Options Shadow DOM Save & 8s Wait)

- **错误现象**：在 `options` 页面填报 `runFullTrust` 理由后，保存按钮未被点击或跳转过快导致后台数据未持久化。
- **根本原因**：Partner Center 底部的【保存】按钮封装在 `he-button` 的 Shadow DOM 内部，且异步保存需要时间完成服务端落库。
- **正确规范**：必须通过 Shadow DOM 穿透递归查找按钮并触发内部原生 button 点击，并在保存后显式等待 8 秒再跳转离开。

