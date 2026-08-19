# Toolchain Command References

主 `SKILL.md` 只描述不随工具版本变化的流程：bootstrap、run、validate、package、publish。

每个版本的具体命令放在独立目录：

```text
references/toolchain/<toolchain-release>/commands.md
```

## Selection rule

1. 读取 `references/version-lock.md` 的 `Command Reference`。
2. 打开对应 `commands.md`。
3. 执行前记录命令文档版本和 `--version` 输出。
4. 命令参数、输出目录或 Store 子命令发生变化时，创建新的 toolchain release；保留旧目录供历史 Build 重现。
5. Windows App SDK、WinUI templates、`winapp` CLI 和命令文档一起完成 Smoke Test 后，才把 `version-lock.md` 的 placeholders 替换为具体值。
