# 一小时执行表

| 时间 | 动作 | 通过条件 |
| --- | --- | --- |
| 0–10 分钟 | entry → portable npm ci → bootstrap/doctor/self-test | 工作区 Node、便携 Git、依赖和 Electron 运行时就绪 |
| 10–30 分钟 | discovery/create/dev | 源码可启动，真实窗口核心闭环可操作 |
| 30–40 分钟 | test + 动态验收 + 用户确认 | 输入、日期、持久化、错误和庆祝路径都有证据 |
| 40–48 分钟 | reserve/package/preflight | 用户确认发布，包和资料不是占位内容 |
| 48–60 分钟 | 六阶段 apply + verify | 每阶段 cold Diff=0，Overview 模块全 Complete |

登录、名称确认、文案和截图属于用户输入时间；每个阶段都保存 checkpoint，窗口关闭后可从当前阶段继续。未取得动态证据时保留未完成状态。
