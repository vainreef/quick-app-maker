# Partner Center Edge 命令行自动化

## 使用入口

统一入口是：

```text
toolchain/edge-store-cli/Invoke-EdgeStore.ps1
```

这是纯 PowerShell 5.1 CLI。它自己启动隔离的 Microsoft Edge，通过本机回环地址上的 Chromium DevTools Protocol 执行 DOM 操作、文件上传、页面验证和阶段恢复。

Agent 不调用 Codex 浏览器工具、浏览器扩展、坐标点击或 OCR，也不把用户日常 Edge profile 作为默认 profile。

## 标准流程

```text
launch
→ 用户在隔离 Edge 中登录 / MFA
→ identity（需要时提取 3 个 Product Identity 参数）
→ inspect（只读结构检查）
→ run（dry-run 计划）
→ run -Phase <phase> -Apply（首轮逐表填报、保存、验证）
→ run -Phase all -Apply（后续可整链恢复）
→ 概览页检查六项完成
→ run -Apply -Submit -ConfirmSubmit（最终提交）
```

每个阶段都是：

```text
打开固定 URL
→ 检查稳定选择器唯一命中
→ 填写或上传
→ 点击保存
→ 读取可见错误区
→ 写入 checkpoint
```

## 用户环境保护

1. 默认 profile 位于 CLI 的 `state/edge-profile/`，与用户日常 Edge 分离。
2. 只绑定 `127.0.0.1`，端口动态分配。
3. 只结束 CLI 自己启动的 Edge PID。
4. 不读取密码、Cookie、Local Storage 或浏览器凭据。
5. 登录、MFA、CAPTCHA 由用户在 Edge 窗口中完成。
6. 失败时停止在原页面，生成结构化 `inspect-*.json`，不猜测按钮。
7. 默认使用 `Manual` 发布模式。
8. 最终提交需要 `-Submit -ConfirmSubmit` 两个显式参数。

## 选择器优先级

Partner Center 页面变化频繁，选择器优先级固定为：

```text
data-l10n-key
data-automation-id
id
name
uitestid
aria-labelledby
```

纯文字选择只作为最后一级，并且必须唯一命中。命中 0 个或多个元素时退出码为 7。

## 过程记录中已固化的规则

来自 `apps/Project-02/process.md` 和 Round 8：

- 必须先进入产品概览页点击「开始提交」，六个表单才出现；
- 定价的 Default 市场组不创建新组：货币下拉选 `CNY - 中国`，价格段选 `0`（¥0）；
- 可见性保持「零售分发」与「开放受众」；
- 属性的隐私区先选「否，我的产品不使用任何个人信息」，再选「提供隐私策略文本」并填写文本框；
- 中文 Store 一览使用 `languageid=5&languagecode=zh-cn`；
- 年龄分级选择 `input[name="question#1109"][value="2558"]`；
- 「其他所有应用类型」后 9 个追问全部选择「否」；
- 年龄分级预览必须勾选 IARC 条款同意框，然后保存，再点「继续」；
- 程序包上传使用 `input.fileuploader`；
- 上传前检查 MSIX 内 `AppxManifest.xml` 的显示名与预留产品名一致；
- 程序包设备系列只保留 Windows 10/11 Desktop；
- Store 一览必填说明和至少一张桌面截图；
- 关键字最多 7 个；
- 提交选项中的 `runFullTrust` 说明框必须填写；
- 包页面的警告和提交选项里的说明框是两个不同流程，不能混为一谈。

## 当前产品配置

Qiangua 的本地配置：

```text
apps/Project-02/qiangua/build/edge-store.json
```

通用配置样例：

```text
toolchain/edge-store-cli/examples/store-automation.json
```

配置文件可以直接引用 `store-listing.md`，CLI 会读取完整描述、简短描述、功能列表和关键词。
