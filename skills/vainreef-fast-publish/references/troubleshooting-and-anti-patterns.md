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
