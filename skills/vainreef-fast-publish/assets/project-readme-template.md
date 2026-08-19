# {{APP_NAME}}

> {{ONE_SENTENCE_VALUE}}

## 项目状态

- 模式：Vainreef Fast Publish Mode
- 需求状态：已与用户确认
- Fast Mode 分类：{{FAST_MODE_CLASSIFICATION}}
- 目标平台：Windows 11 优先，Windows 10 1809+
- 分发：MSIX + Microsoft Store
- 价格：Free

## 这个项目要做什么

{{PROJECT_OVERVIEW}}

## 为什么要做

### 用户

{{TARGET_USER}}

### 真实使用场景

{{REAL_SCENARIO}}

### 要解决的问题

{{PROBLEM}}

## 用户完整流程

1. {{JOURNEY_STEP_1}}
2. {{JOURNEY_STEP_2}}
3. {{JOURNEY_STEP_3}}
4. {{JOURNEY_RESULT}}

## V1 功能

### Must have

- [ ] {{MUST_HAVE_1}}
- [ ] {{MUST_HAVE_2}}
- [ ] {{MUST_HAVE_3}}

### Later

- {{LATER_1}}
- {{LATER_2}}

## 输入、输出与数据

| 项目 | 设计 |
| --- | --- |
| 输入 | {{INPUTS}} |
| 输出 | {{OUTPUTS}} |
| 本地存储 | {{LOCAL_STORAGE}} |
| 数据规模 | {{DATA_SCALE}} |
| 导入/导出 | {{IMPORT_EXPORT}} |
| 删除/重置 | {{DELETE_RESET}} |

## 网络、账号与密钥

| 项目 | 设计 |
| --- | --- |
| 离线能力 | {{OFFLINE_BEHAVIOR}} |
| 网络服务 | {{NETWORK_SERVICES}} |
| API key 所有者 | {{API_KEY_OWNER}} |
| 账号与同步 | {{ACCOUNT_SYNC}} |

## 权限与隐私

- 文件/设备权限：{{PERMISSIONS}}
- 个人信息数据流：{{PERSONAL_DATA_FLOW}}
- 遥测：默认关闭
- 广告追踪：默认关闭
- 用户内容上传：{{CONTENT_UPLOAD}}
- Privacy Policy 判断：{{PRIVACY_POLICY_DECISION}}

## Fast Mode 判断

### 保留的核心价值

{{PRESERVED_VALUE}}

### 能力边界结论

{{BOUNDARY_EXPLANATION}}

### 用户确认过的调整

{{APPROVED_PROJECTION}}

## 固定技术栈

- C#
- .NET 10 LTS
- WinUI 3 + XAML
- Windows App SDK Stable / pinned
- `dotnet` CLI
- `winapp` CLI
- MSIX
- Microsoft Store
- `System.Text.Json`；复杂本地数据按需使用 `Microsoft.Data.Sqlite`
- XAML + code-behind + 简单 Service 层

## 目录计划

```text
{{APP_SLUG}}/
├── README.md
├── App.xaml
├── MainWindow.xaml
├── Pages/
├── Models/
├── Services/
├── Storage/
├── Assets/
├── Package.appxmanifest
├── build/
└── store/
```

## 验收标准

- [ ] {{ACCEPTANCE_1}}
- [ ] {{ACCEPTANCE_2}}
- [ ] {{ACCEPTANCE_3}}
- [ ] Debug 运行通过
- [ ] Release 构建通过
- [ ] MSIX 安装、启动、关闭、重开与卸载通过

## Microsoft Store 发布计划

1. 确认应用名称与 Package Identity。
2. 准备图标、截图、描述和年龄评级。
3. 检查 Capability 与隐私资料。
4. 生成并安装测试 MSIX。
5. 生成 Store submission package。
6. 用户确认 Partner Center 信息后提交。

## Decisions

| 日期 | 决策 | 原因 | 用户确认 |
| --- | --- | --- | --- |
| {{DATE}} | {{DECISION}} | {{DECISION_REASON}} | Yes |

## Open questions

- {{OPEN_QUESTION_OR_NONE}}
