# 一小时执行表

| 时间 | 动作 | 通过条件 |
| --- | --- | --- |
| 0–10 分钟 | entry → portable npm ci → bootstrap/doctor/self-test | 工作区 Node、便携 Git、依赖和 Electron 运行时就绪 |
| 10–30 分钟 | discovery → create → 业务编码 (HTML/JS/CSS) → test | 业务功能完整实现，自动化测试秒级通过 |
| 30–40 分钟 | dev 启动窗口 + 用户亲自试用与确认 | 用户在真实窗口中完成体验，对功能与持久化确认满意 |
| 40–48 分钟 | reserve/package/preflight | 用户确认发布，包和资料不是占位内容 |
| 48–60 分钟 | 按状态执行未完成阶段 + verify | 现有总检通过，图片由用户确认 |

登录、名称确认、文案和截图属于用户输入时间；每个阶段都保存 checkpoint，窗口关闭后可从当前阶段继续。以用户亲自试用体验的确认结论为准。
