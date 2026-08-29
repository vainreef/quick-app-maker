# Windows Build Notes

这里保存 Agent 在真实 Windows 环境中使用工具链的命令和经验。

## Sources of truth

- 安装版本和下载地址：仓库根目录的 `bootstrap/toolchain.json`。
- 当前 release 的命令与实测坑点：`references/toolchain/<release>/commands.md`。
- 每次实机验证结果：`docs/windows-smoke-test.md` 中的测试记录。

版本数字只在 `bootstrap/toolchain.json` 维护。命令文档读取该文件并记录实测输出，避免再维护一份重复的 version lock。

## What belongs in commands.md

- 环境检查命令。
- 创建、运行、构建、发布和打包命令。
- 命令的真实退出码与关键输出。
- Windows PowerShell 5.1、路径、模板和 MSIX 相关坑点。
- 错误原文、复现条件和经过复测的解决步骤。
- 当前仍待实测的命令。

## Learning rule

先按最直接的命令执行。遇到错误后：

1. 保存命令、stdout、stderr 和退出码。
2. 记录 Windows build 与工具版本。
3. 修复并重新执行。
4. 同一问题复现并解决后，再将规律写进 `commands.md`。

文档记录实测经验，不提前推演大量假设性限制。

## Release directories

工具命令发生明显变化时创建新目录：

```text
references/toolchain/v1/commands.md
references/toolchain/v2/commands.md
```

旧目录保留历史经验，新项目读取 `bootstrap/toolchain.json` 中的 `release` 选择对应目录。
