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

## 每次执行前的顺序

1. 先读 manifest 和 Listing Markdown；确认 `values.description`、`shortDescription`、`features`、`keywords` 都已导入。
2. 执行 `-Action preflight`。
3. 已有浏览器时执行 `-Action inspect`，确认 `PageKind`、URL、标题、错误和当前按钮；`inspect` 不导航。
4. 只在需要登录或新会话时执行 `-Action launch`。
5. 续跑使用 `-Phase X`；checkpoint 中有 submissionId 时直接进入 X。不要用 `run -Phase all` 代替状态检查。

## 收敛规则

- `0 form differences` 不代表产品完成。
- **配置变更前置铁律**：当用户要求修改线上表单字段（如“把隐私政策改成否”）时，Agent **必须首先修改本地 Desired State JSON 文件**，再调用 CLI 执行收敛。严禁直接执行 CLI 导致 0 差异空转退出。
- `AppliedUnverified` 不代表保存成功。
- 保存后必须冷加载重新观察；若保存跳回概览，先重新导航到该表再验证。
- **概览模块状态真值标准**：`<app-module-status>` 容器内无 badge 文本即判定为 `Complete`；包含 `<he-badge class="text-non-started">未启动</he-badge>` 为 `Incomplete`。严禁因缺失“完成”文本而 fallback 到 Unknown。
- 最终必须读取概览模块状态；`Unknown`、`Incomplete`、`Processing`、`Error` 都不通过。
- 只有概览证据写入 checkpoint 后才是 `PRODUCT_VERIFIED`。
- 未加 `-Apply` 发现差异时退出码为 4，禁止写入完成状态。

## 页面/控件与排障规则

- 先识别页面类型，再等待页面控件；SPA 空壳只算 LoadingShell。
- 每次 Lit/Angular 动作后重新查询节点；不缓存节点、文件 input 索引或坐标。
- 每个 toggle 只发一次物理 click；不混合 mousedown、mouseup、click 和 `.click()`。
- 文件上传使用 `DOM.describeNode` + `DOM.setFileInputFiles` 穿透 Shadow DOM，且 MSIX 必须包含完整 Assets。
- 文件名出现只代表文件名出现；包必须达到唯一 `Validated`。
- 同名 Error 包或重复包阻止新上传；清理完毕后必须冷加载刷新页面使“保存”按钮激活。
- 语言网格按 `Action-{id}` 和 `Name-{id}` 配对，目标语言由 `supportedLanguageCodes` 声明。
- 遇到未知表单阻塞时，优先执行 `-Action dumpdom` 提取清晰结构化 DOM，或调用针对性急救指令（`answerno`、`fixpackage`、`canceluploads`、`fixprivacy`），禁止盲目全表重跑。

## 退出和报告

Agent 报告必须包含：命令、phase、PageKind、URL、PLAN、动作结果、冷加载 PLAN、概览模块状态、checkpoint 状态和退出码。只写“按钮点到了”“EXIT=0”“CONVERGED”而没有这些证据，视为报告不完整。
