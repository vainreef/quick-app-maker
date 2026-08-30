# 一小时执行表

| 时间 | 动作 | 结果 |
| --- | --- | --- |
| 0–10 分钟 | bootstrap/doctor/npm cache | Node 和依赖就绪 |
| 10–30 分钟 | discovery/create/dev | MVP 可试用 |
| 30–40 分钟 | test + 用户确认 | 验收通过 |
| 40–48 分钟 | reserve/package/preflight | Store 包就绪 |
| 48–60 分钟 | 六阶段 apply + verify | 资料全绿，交给用户提交 |

登录、名称确认、文案和截图属于用户输入时间；每个阶段都保存 checkpoint，窗口关闭后可从当前阶段继续。
