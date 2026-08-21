# Skills

每个 Skill 独立成一个目录，核心入口为 `SKILL.md`。可复用脚本放在 `scripts/`，按需加载的资料放在 `references/`，模板和视觉资源放在 `assets/`。

当前 Skill：`vainreef-fast-publish`。

它从“用户想做什么”开始进行 Progressive Discovery，用户给出第一段实质想法后创建项目文件夹和 living README。仓库提供 Windows 工具链、实测命令、Smoke Test 和经验记录；Agent 根据每个 App 的需求现场设计代码、结构和依赖。版本数字统一读取根目录 `bootstrap/toolchain.json`，Windows 命令和实际坑点维护在 `references/toolchain/v1/commands.md`。Capability 资料只是建议，详细命令与日志写入项目的 `build/run-report.md`。
