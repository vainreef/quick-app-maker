# Skills

每个 Skill 独立成一个目录，核心入口为 `SKILL.md`。可复用脚本放在 `scripts/`，按需加载的资料放在 `references/`，模板和视觉资源放在 `assets/`。

当前 Skill：`vainreef-fast-publish`。

它从“用户想做什么”开始进行创意访谈，用户给出第一段实质想法后立即创建项目文件夹和 living README，并在每轮对话后更新用户需要、目前省略、正在探索、已经确定、当前限制和用户选择。创意提问与可行性思考同步进行，明显风险在出现当轮就解释和确认。用户主动收口后，Agent 用通俗产品语言确认完整项目，随后推进生成、运行、校验、打包与 Store 提交。访谈流程位于 `skills/vainreef-fast-publish/references/discovery-interview.md`，能力边界位于 `skills/vainreef-fast-publish/references/capability-boundary.md`，具体版本锁定记录位于 `skills/vainreef-fast-publish/references/version-lock.md`。
