# Capability Suggestions

这里是 Agent 的经验提示板，不是依赖准入表。

Agent 可以根据当前 App 自由选择 PDF、图片、CSV、Office、压缩包、媒体、OCR、二维码、本地 AI 或其他实现。仓库里已有的建议用于减少重复研究；缺少条目时直接研究、实现和实测。

## How to use this file

1. 先理解当前 App 真正需要的输入、处理和输出。
2. 优先查看 .NET、Windows API 和当前工具链已经具备的能力。
3. 根据功能质量、开发速度、打包结果和维护成本选择实现。
4. 在真实项目里执行 build、run 和 MSIX 测试。
5. 将可靠经验追加到本页，或新建一个简短的 capability note。

## Useful questions

选择额外依赖时，可以检查：

- 包是否支持当前 .NET 和 win-x64。
- 是否带 native DLL、模型、字体、CLI 或额外进程。
- Publish 和 MSIX 后文件是否完整。
- 临时文件、路径、退出码和错误信息如何处理。
- 许可证是否需要 NOTICE 或 attribution。
- 实际安装体积、速度和内存表现。
- Windows Store 打包过程中是否出现新问题。

这些问题用于帮助判断，不承担阻塞职责。

## Suggested implementation ladder

以下顺序只是常见的尝试顺序：

1. .NET / Windows 内置能力。
2. 维护活跃的 managed NuGet。
3. native-backed NuGet。
4. 随 App 打包的 CLI 或模型。
5. 随 App 打包的额外 runtime。
6. 最适合当前产品的其他方案。

Agent 可以根据效果和实测结果跳过层级。

## Experience notes

当前尚未写入经过 Windows/MSIX 实测的具体库。第一次真实 App 使用某项能力后，建议记录：

```markdown
## CAPABILITY_NAME

- App / use case:
- Chosen implementation:
- Package and version:
- Why it was chosen:
- Build result:
- Publish/MSIX result:
- Runtime files or subprocesses:
- License note:
- Observed Windows issues:
- Commit / date tested:
```

只记录真实使用过的经验，避免维护大量空白 YAML。
