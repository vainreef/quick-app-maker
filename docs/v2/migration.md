# V1 → V2 迁移

旧 C#/.NET/WinUI/CDP 实现已从主线移除。替换关系：

| V1 | V2 |
| --- | --- |
| WinUI/XAML 工程 | Electron/Vue Runtime |
| dotnet build | 源码直接运行 |
| PowerShell launcher | `node bin/qam.mjs` |
| C# CDP client | Playwright Locator |
| 固定 Task.Delay | Locator assertion / bounded poll |
| 手写 checkpoint | schema 校验 + 原子写 |
| 弱 preflight | MSIX/manifest/assets 完整预检 |
| 手动 HTML 快照 | 可执行 fixture |

迁移顺序：

1. portable Node/bootstrap；
2. generator + no-build template；
3. packager/MSIX/preflight；
4. Playwright Store phases；
5. Skill/README/smoke test；
6. 删除所有旧树。
