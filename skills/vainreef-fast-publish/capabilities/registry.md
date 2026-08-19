# Capability Registry

Capability Registry 是 Fast Publish 的依赖入口。Agent 先按用户需求匹配能力，再读取对应目录的 `capability.yml`。

## Status values

- `planned`：能力名称已登记，实施方案等待 Windows/x64/MSIX 验证。
- `candidate`：有候选实现，正在做许可证、打包和运行验证。
- `verified`：已记录 preferred implementation、精确版本、许可证、Level、x64/MSIX 测试与 fallback。
- `retired`：历史实现，禁止新项目采用。

## Initial registry

| Capability | Path | Status | Default Level |
| --- | --- | --- | --- |
| PDF | `capabilities/pdf/capability.yml` | planned | 1/2 |
| Image | `capabilities/image/capability.yml` | planned | 0/1/2 |
| CSV | `capabilities/csv/capability.yml` | planned | 0/1 |
| Office | `capabilities/office/capability.yml` | planned | 1/2/3 |
| Archive | `capabilities/archive/capability.yml` | planned | 1/2/3 |
| Media | `capabilities/media/capability.yml` | planned | 2/3 |
| OCR | `capabilities/ocr/capability.yml` | planned | 2/3/4 |
| QR Code | `capabilities/qrcode/capability.yml` | planned | 1/2 |
| Local AI | `capabilities/ai-local/capability.yml` | planned | 2/3/4 |

## Entry acceptance

将条目标记为 `verified` 前，完成：

- preferred implementation 与 fallback。
- Dependency Level 与精确版本。
- 许可证、NOTICE 要求和分发方式。
- x64 构建、MSIX 打包、安装、启动、卸载与重装。
- 运行时文件、子进程、临时目录和用户数据行为。
- Store manifest、权限、资源体积和性能检查。
- 失败场景与用户可理解的提示。
- 一份 Technical Run Report。
