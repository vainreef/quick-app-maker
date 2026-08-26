# Agent 运行契约（Edge Store）

这是执行规则，不是过程日志。Agent 每次接手发布任务都以本文件和
`docs/partner-center/Edge-Store-可靠性重构.md` 为准，不从旧 `process.md` 的历史命令推导当前实现。

## 唯一正式入口

只使用：

```text
toolchain/edge-store-cli/Invoke-EdgeStore.ps1
toolchain/edge-store-cli/EdgeStore.Cli.csproj
```

`apps/Project/edge-store-cli-fast/` 是上一轮诊断副本和生成物，不是正式运行时。里面的 DLL、`_tmp-diag` 脚本和旧 README 都不参与发布流程。不要从 process.md 复制 `edge-store-cli-fast/bin/.../EdgeStore.Cli.dll` 命令。

## 核心发布铁律：严禁一个脚本一冲到底

发布提审必须严格拆解为 **独立离散阶段（Store 0 ~ Store 8）** 执行。**每步执行完毕后，必须自动或手动调用阶段专属 DOM 探针进行深度自检，断言确认完好后再进入下一步。**

```text
阶段 0: 静态离线预检              (-Action preflight)
阶段 1: 独立常驻浏览器拉起与登录态确权 (-Action launch / -Action inspect)
阶段 2: 概览页动态 Submission 探测    (-Action discover)
阶段 3: 定价与可用性离散步进与自检      (-Action step -Phase availability)
阶段 4: 应用属性离散步进与自检          (-Action step -Phase properties)
阶段 5: 年龄分级离散步进与自检          (-Action step -Phase ageRatings)
阶段 6: 程序包上传与状态自检          (-Action step -Phase packages)
阶段 7: 应用商店一览离散步进与自检      (-Action step -Phase listing)
阶段 8: 提审选项离散步进与自检          (-Action step -Phase options)
阶段 9: 概览页 6 大模块冷加载总检      (-Action verify)
阶段 10: 显式双确认提交审核          (-Action submit -ConfirmSubmit)
```

## 每次执行前的顺序与步进规范

1. 先读 manifest 和 Listing Markdown；确认 `values.description`、`shortDescription`、`features`、`keywords` 都已导入。
2. 执行 `-Action preflight` 进行静态质检。
3. 执行 `-Action launch` 启动独立常驻 Edge，并在 `WinSta0\Default` 桌面确权。
4. 执行 `-Action discover` 动态探测 `submissionId` 和 6 大表单链接并建立 DOM 基线。
5. 针对 6 大表单依次执行单步步进 `-Action step -Phase <phase>`，每一阶段执行后必须检查输出的 `[DOM-PROBE]` 结构化证据。
6. 全阶段步进完成后，执行 `-Action verify` 确认概览页全绿勾且无「未启动」模块。
7. 经用户确认后，方可执行最终提审 `-Action submit -ConfirmSubmit`。

## 收敛与 DOM 自检规则

- `0 form differences` 不代表产品完成。
- **配置变更前置铁律**：当用户要求修改线上表单字段（如“把隐私政策改成否”）时，Agent **必须首先修改本地 Desired State JSON 文件**，再调用 CLI 执行收敛。严禁直接执行 CLI 导致 0 差异空转退出。
- `AppliedUnverified` 不代表保存成功。
- **强制阶段后 DOM 探针（DOM Self-Inspection）**：每个 Phase 保存后，必须由探针在 DOM 中提取真实渲染的文本、复选框态、下拉框值和图片数量进行双向断言，断言不符直接中断报错。
- **概览模块状态真值标准**：`<app-module-status>` 容器内无 badge 文本即判定为 `Complete`；包含 `<he-badge class="text-non-started">未启动</he-badge>` 为 `Incomplete`。严禁因缺失“完成”文本而 fallback 到 Unknown。
- 最终必须读取概览模块状态；`Unknown`、`Incomplete`、`Processing`、`Error` 都不通过。
- 只有概览证据与 DOM 探针快照写入 checkpoint 后才是 `PRODUCT_VERIFIED`。
- 未加 `-Apply` 发现差异时退出码为 4，禁止写入完成状态。

## 页面/控件与排障规则

- 先识别页面类型，再等待页面控件；SPA 空壳只算 LoadingShell。
- 每次 Lit/Angular 动作后重新查询节点；不缓存节点、文件 input 索引或坐标。
- 每个 toggle 只发一次物理 click；不混合 mousedown、mouseup、click 和 `.click()`。
- 文件上传使用 `DOM.describeNode` + `DOM.setFileInputFiles` 穿透 Shadow DOM，且 MSIX 必须包含完整 Assets。
- 文件名出现只代表文件名出现；包必须达到唯一 `Validated` 且退回概览页确权显示完成。
- **模块化架构与指令集**：CLI 入口 `Program.cs` 保持在 300 行以内，各阶段独立封装于 `Commands/`（`preflight`, `launch`, `step`, `discover`, `inspect`, `dumpdom`, `cleanpackages`, `cleanlanguages`, `filllisting`, `verify`, `stop`）。
- **语言列表管理铁律**：进入 Store 一览若遇多语言网格（Manage Languages），必须通过组件级批量删除非目标语言并在 `managelanguages` 底部点击「保存」，严禁单项慢速轮询导致超时。
- **导航安全铁律**：严禁通过模糊锚点 `a[href*="/overview"]` 导航回概览页（防止误跳外部 Learn 文档），必须使用绝对构造的 `${baseUrl}/${productId}/overview` 控制台 URL 强制跳转。

## 退出和报告

Agent 报告必须包含：命令、phase、PageKind、URL、PLAN、动作结果、冷加载 PLAN、概览模块状态、checkpoint 状态和退出码。只写“按钮点到了”“EXIT=0”“CONVERGED”而没有这些证据，视为报告不完整。
