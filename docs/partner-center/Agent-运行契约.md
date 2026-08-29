# Microsoft Partner Center 商店发布 Agent 运行契约

本文档定义 Partner Center 自动化的执行边界和完成语义。开始发布前同时读取：

1. 本文件；
2. `docs/partner-center/Edge-Store-可靠性重构.md`；
3. `skills/vainreef-fast-publish/SKILL.md`。

## 唯一实现入口

所有发布、排障和诊断操作统一使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action <action> `
  -Manifest .\<app>\build\edge-store.json `
  -StateDir .\.cache\edge-store-state
```

- 正式源码：`toolchain/edge-store-cli/`。
- `apps/Project/edge-store-cli-fast/`、历史 DLL、`process.md` 临时命令和 `_tmp-diag` 脚本都不属于当前实现。
- 所有状态、日志、下载和缓存均保存在 `WORKSPACE_ROOT` 内。

## 标准离散流水线

每个阶段单独执行、单独审查证据。只有当前阶段达到完成语义后才进入下一阶段。

| 阶段 | 命令 | 结果要求 |
| --- | --- | --- |
| STORE -1 名称预留 | `-Action reserve -AppName "<AppName>"` | 自动检查可用性、预留名称、提取 ProductId 和三项包身份并回填本地文件 |
| STORE 0 离线预检 | `-Action preflight` | MSIX、manifest、语言、文案和资产通过静态校验 |
| STORE 1 会话 | `-Action launch -KeepOpen` | 独立 Edge 会话可连接，用户已完成登录 |
| STORE 2 基线发现 | `-Action discover` | 从当前产品概览提取实时 submissionId 与表单 URL |
| STORE 3 定价 | `-Action step -Phase availability -Apply` | 冷加载回读无差异，概览模块为 `Complete` |
| STORE 4 属性 | `-Action step -Phase properties -Apply` | 冷加载回读无差异，概览模块为 `Complete` |
| STORE 5 年龄分级 | `-Action step -Phase ageRatings -Apply` | 问卷与条款持久化，概览模块为 `Complete` |
| STORE 6 程序包 | `-Action step -Phase packages -Apply` | 只有一行同名包且状态为 `Validated`，概览模块为 `Complete` |
| STORE 7 商店一览 | `cleanlanguages` 后执行 `filllisting` | 目标语言、文案、关键词和视觉资产持久化，概览模块为 `Complete` |
| STORE 8 提交选项 | `inspectoptions`，审查后执行 `filloptions` | 发布模式和受限功能说明持久化，概览模块为 `Complete` |
| STORE 9 总检 | `-Action verify` | 冷导航后的概览页六大模块均为 `Complete` |
| STORE 10 提交交付 | 用户在浏览器中复核并点击提交 | Agent 汇报证据并结束自动填报 |

`inspect` 与 `status` 是状态探针。`run -Phase all` 是明确的六阶段写操作，不作为状态查询。

## 完成语义

以下结果都只是中间证据：

- 命令退出码为 0；
- 点击动作成功；
- 当前 DOM 与目标值相等；
- 页面显示文件名；
- 表单差异暂时为 0。

阶段完成链必须是：

```text
PageKind 已识别
→ ObservedState 完整
→ 完整阶段 Diff 为零
→ 冷导航重新加载
→ 重新 Observe 与 Diff 仍为零
→ 对应 Submission Overview 模块为 Complete
→ checkpoint 写入 Converged
```

只读运行发现差异时返回退出码 4，不写入完成状态。`Unknown`、`Processing`、错误页、加载骨架和 selector 歧义都保持未完成。

## 页面与等待规则

1. 等待控件前先识别 `PageKind`。
2. `inspect` 只读当前页面，不导航。
3. SPA 持久化验证使用当前绝对 URL 的冷导航，不使用 `Page.reload` 作为最终证据。
4. 表单控件存在性与可见性分开判断；隐藏的原生 input 仍可能是有效业务控件。
5. 每个等待都有具体信号、截止时间和进度心跳；首个谓词异常进入诊断证据。
6. 节点与 runtime ID 只在当前动作内有效；页面变化后重新定位。

## 上传与组件规则

- 包状态按“文件名 + 状态”建模。
- 同名包为 `Processing` 时只等待；为 `Validated` 时跳过上传；出现 `Error` 或重复行时停止本阶段并清理冲突。
- 上传成功的充分证据是唯一同名行达到 `Validated`，随后冷加载回读且概览模块完成。
- Shadow DOM 文件 input 每次重新解析；设置文件后派发 `input` 与 `change` 事件。
- `he-select`、`he-checkbox` 等响应式组件使用真实交互事件，并在动作后重新读取状态。
- 大型 SPA 使用浅 DOM、局部 `requestNode` 和 Accessibility Tree；避免整棵深 DOM 序列化。

## 业务不变量

- manifest 的两个 `DisplayName` 与 Partner Center 实际预留名称完全一致。
- manifest 明确声明目标资源语言和 `Windows.Desktop` 设备系列。
- 关键词最多 7 个；保存前先清理旧标签并核对最终数量。
- Listing 截图和 Logo 上传后，页面必须出现缩略图或数量变化再保存。
- `runFullTrust` 存在时，提交选项填写对应用途说明。
- 最终提交按钮由用户亲自点击。

## 编译与文件锁

修改 Edge Store CLI 源码后：

1. 停止本轮记录的精确 CLI PID；
2. 执行 `dotnet build-server shutdown`；
3. 确认没有进程仍在写同一 `bin/obj`；
4. 仅在明确的增量缓存或锁错误后清理输出目录；
5. 前台执行：

```powershell
dotnet build toolchain/edge-store-cli/EdgeStore.Cli.csproj `
  -c Release `
  --nologo `
  /p:UseSharedCompilation=false
```

编译、运行和状态轮询保持分离，避免后台编译与同目录重建重叠。

## 每阶段汇报字段

1. 当前阶段与实际命令；
2. 页面 URL、标题和 `PageKind`；
3. Observe/Diff/冷加载回读结果；
4. 对应概览模块现场状态；
5. 错误区域、包行或关键控件证据；
6. 退出码与 checkpoint 状态。
