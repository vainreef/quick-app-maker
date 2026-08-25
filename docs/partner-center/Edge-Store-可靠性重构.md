# Edge Store 自动化可靠性重构

执行时先读取 `Agent-运行契约.md`。`process.md` 是上一轮事实记录，里面的命令和临时副本路径不具有当前实现的权威性。

## 结论

本轮暴露的不是 selector 数量不足，而是原实现把四种不同事实合并成了一个 `CONVERGED`：

1. 驱动命令成功；
2. 当前 DOM 看起来等于目标；
3. 服务端已经持久化；
4. Partner Center 概览认可该模块完成。

这四层必须逐层给证据，不能互相替代。新的唯一完成链为：

`页面类型已识别 → Observed 完整 → Diff 为零 → 冷加载回读为零 → 概览模块 Complete → checkpoint Converged`

## 从 process.md 定位到的代码级根因

- 实际运行中临时修复写进了 `apps/Project/edge-store-cli-fast/`，正式入口仍指向 `toolchain/edge-store-cli/`。两个目录已经全量漂移；坐标反序列化和浅 DOM 修复没有进入正式工具。
- `Program` 每次都 `new StoreCheckpoint`，原 checkpoint 没有加载，因此所谓 resume 只写不读。
- `RunAsync` 无条件执行 preflight、登录、概览 discovery；所以单表 phase 仍从 Store 0 开始。
- 旧 `ReconcilePhaseAsync` 在委托返回后无条件 `MarkConverged`。只读模式发现 diff 后 `return`，外层仍然记完成。
- 多个 `VerifyAsync` 只 reload/observe/error scan，没有重新计算 diff；Listing、Packages、Options 都可能假验证。
- Listing 临时版把“发现语言网格后导航”放在 `ObserveAsync` 的等待谓词中。验证再次调用 Observe 时会再次导航，形成刷新/跳转循环。
- Packages 的 diff 每轮无条件计划上传；Apply 又用 body 中出现文件名作为完成判据。这同时造成不幂等和“文件名出现即成功”。
- 概览没有结构化模块状态模型，最后靠 `body.innerText` 顺序猜六项归属。
- `Waiter` 吞掉谓词异常，30 秒改成 300 秒只会把确定性 selector 错误放大成五分钟静默。

## 现在的结构

### 1. 页面状态是一级模型

`PageInspector` 输出 `PageSnapshot`，明确区分：

- LoadingShell / SignIn / ErrorPage
- ProductOverview / SubmissionOverview
- 六个表单页
- ListingLanguageGrid / ListingForm
- AgeRatingsQuestionnaire / AgeRatingsSummary
- SubmissionConfirmation / CertificationStatus

任何 adapter 只在自己的页面类型上执行。`inspect` 只读输出 URL、标题、页面类型、信号、按钮、错误；它不导航。

### 2. 完成状态采用证据分级

checkpoint 状态为：

`Unknown → Observed → NeedsChanges → Applying → AppliedUnverified → Converged / Failed`

`AppliedUnverified` 明确表示“动作执行了，但产品尚未证明成功”。只有概览页模块显式为 Complete，才写入 `convergedPhases`。

### 3. Overview 是最终裁判

`OverviewAdapter` 不按 body 文本顺序切片；它以每个模块的 submission href 为锚点，在局部容器内读取：

- Incomplete / 未完成
- Processing / 上传中 / 验证中
- Error / failed
- Complete / Validated / success / check icon

Unknown 也不通过。缺少完成证据和明确失败同样会阻止 checkpoint 收敛。

### 4. 上传按服务端状态幂等

包状态模型为 `文件名 + 状态`：

- 0 行：允许上传一次；
- 1 行 Processing：只等待，不重传；
- 1 行 Validated：跳过上传；
- Error 或重复行：停止本阶段，新上传不会继续扩大污染。

文件 input 在每次使用前用 `Runtime.evaluate → DOM.requestNode` 重新解析，禁止保存数组索引。完成条件是唯一同名行达到 Validated，不是页面出现文件名。

### 5. Lit/Shadow DOM 的交互规则

- 禁止 `DOM.getDocument(depth:-1,pierce:true)`；只取 depth=1 根，再局部 requestNode。
- 节点只在一次动作内有效；动作后重查。
- 每次 toggle 只发一次物理 click。
- 语言网格按 `Action-{id}` 与 `Name-{id}` 配对，点击后等待 Lit 重绘，再查下一项。
- 文件 input 与图片槽位按局部上下文匹配，不按全页第 N 个控件。

### 6. 目标态和校验规则同源

- Markdown 同时识别中英文标题及括号混合标题。
- Description、Short Description、Features、Screenshot 在 preflight 中做正向非空校验。
- `privacy=No` 不要求不存在的隐私文本/URL；`privacy=Yes` 才要求 URL。
- MSIX 声明 runFullTrust 时才要求用途说明。
- `0 <= 7` 不再被当成“关键词导入成功”；严格模式要求文案导入产物非空。

## 40 个现象对应的控制点

| 问题组 | 对应现象 | 控制点 |
|---|---|---|
| CDP 协议 | 1、3、25、26、27 | 大小写反序列化、按 id 分发、awaitPromise、浅 DOM、每请求 deadline、WebSocket keepalive |
| 页面识别 | 2、4、5、17、18、19、23 | PageInspector、显式 PageKind、局部 Shadow DOM、动作后重查 |
| 输入原语 | 6、7、12、29 | 单次物理 click、原型 setter + input/change/blur、动态 requestNode、复杂 JS 留在 C# raw string |
| 上传幂等 | 8、9、24 | 文件名+状态模型、Validated 才完成、重复/Error 阻断、冷加载验证 |
| 收敛语义 | 10、11、13、37、38、40 | AppliedUnverified、重新 Diff、OverviewAdapter、Unknown 不通过 |
| 业务规则 | 14、15、16、20、21、22 | 条件校验、双语 Markdown、正向非空、supportedLanguageCodes |
| CLI/流程 | 30、31、32、33、34、35、36、39 | inspect/status、参数兼容、单表直达、preflight 独立、一次进程、提交固定三步 |
| 编码/诊断 | 28、2、27 | C#/PowerShell UTF-8、WAIT 心跳、首个谓词异常、checkpoint evidence |
| 实战进阶 | 41、42、43、44、45 | Overview 无 badge 即完成判定、Privacy=No 免文本框联动、Shadow DOM ObjectId 文件绑定、MSIX Assets 拷贝前置质检、7 大原地急救诊断动作 (dumpdom/answerno/fixpackage/canceluploads) |

## 命令语义

- `preflight`：仅离线验证，不打开浏览器。
- `launch`：启动/复用隔离浏览器并等待登录。
- `inspect`：只读当前页面，不导航。
- `status`：会话、checkpoint、当前页面状态。
- `dumpdom`：清洗并导出结构化控制台 DOM 快照（`dom-dump-LIVE.html`），剥离无用标签，保留控件与状态。
- `answerno`：原地秒选 9 道年龄分级题、勾选条款并保存（不刷新不跳转）。
- `fixpackage` / `canceluploads`：清理死锁/冲突包并重新上传/激活保存按钮。
- `run -Phase X`：只执行 X；有 checkpoint submissionId 时直接进入该表。
- `run -Phase all`：明确执行六表；不再被描述成登录检查。
- 不带 `-Apply` 发现差异返回 4，不写完成。
- `-Submit -ConfirmSubmit -Apply`：概览六项全部完成后，执行提交按钮、确认按钮、认证状态三步。

## 验收门槛

合并前至少执行：

```text
dotnet build toolchain/edge-store-cli/EdgeStore.Cli.csproj -c Release
dotnet run --project toolchain/edge-store-cli-tests/EdgeStore.Cli.Tests.csproj -c Release
git diff --check
```

Windows 实页回归必须包含：已完成年龄分级、半上传包、同名 Error 包、Listing 语言网格、Listing 冷保存、SPA 空壳、断开 CDP 七种 fixture。任何一项只能以 `PRODUCT_VERIFIED` 或带 PageSnapshot/evidence 的失败结束。
