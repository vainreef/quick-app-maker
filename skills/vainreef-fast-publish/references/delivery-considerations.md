# Delivery Considerations

这些内容帮助 Agent 提前看到工作量和发布变量。它们是设计提示，不是停止条件。

## Questions worth checking

- App 面向哪些 Windows 版本和 CPU 架构。
- 数据留在本机、访问公开 API，还是需要项目自己的在线服务。
- 用户凭据、项目凭据和日志分别放在哪里。
- 文件、相机、位置、麦克风、管理员权限等能力如何向用户解释。
- 依赖是否带 native DLL、CLI、模型或额外 runtime。
- App 采用免费、付费、订阅、广告或其他商业模式。
- MSIX、Store listing、隐私说明和年龄评级需要哪些资料。
- 功能规模是否适合当前时间、机器和测试环境。

## Agent behavior

1. 先理解用户想保留的核心体验。
2. 根据当前项目选择实际可行的工程方案。
3. 对明显增加成本、权限、在线服务或发布步骤的部分，用日常语言说明影响。
4. 给出一到两个实现方向，让用户选择产品体验。
5. 将决定记录进项目 README，然后继续制作。

Agent 可以采用服务器、云同步、第三方 API、native library、CLI、额外 runtime 或其他技术，只要它们服务于用户确认的产品，并在构建与发布阶段完成对应验证。

## Useful delivery patterns

- 本地工具：文件选择器、本地计算、本地 JSON/SQLite。
- API Client：公开 API 或用户填写自己的凭据。
- 项目在线服务：客户端加项目后端，补充部署、密钥和运维记录。
- 媒体/文档工具：managed library、native-backed package 或 bundled CLI，以实际效果和打包测试选择。
- 高权限工具：说明权限用途，测试安装、运行、更新和 Store 资料。
- 多平台产品：拆分各平台宿主与共享业务层，分别记录构建路径。

## Record after implementation

在 `build/run-report.md` 记录最终选择、依赖、权限、在线服务、失败处理、包体积和实测结果。文档以真实工程结论为主。
