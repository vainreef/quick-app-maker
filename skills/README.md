# Skills

每个 Skill 独立成一个目录，核心入口为 `SKILL.md`。可复用脚本放在 `scripts/`，按需加载的资料放在 `references/`，模板和视觉资源放在 `assets/`。

当前 Skill：`vainreef-fast-publish`。

它从“用户想做什么”开始进行 Progressive Discovery，用户给出第一段实质想法后立即创建项目文件夹和 living README，并在每轮对话后更新用户需要、目前省略、正在探索、已经确定、当前限制和用户选择。可行性思考同步进行，Soft Limit 记录风险后继续，Hard Boundary 触发 Advanced Mode 并暂停 Fast Mode 工程动作。Capability Registry 管理 PDF、图片、媒体、OCR、Office 等依赖能力；用户输出保持简单，详细命令与日志写入 `build/run-report.md`。访谈流程位于 `skills/vainreef-fast-publish/references/discovery-interview.md`，能力边界位于 `skills/vainreef-fast-publish/references/capability-boundary.md`，具体版本锁定记录位于 `skills/vainreef-fast-publish/references/version-lock.md`。
