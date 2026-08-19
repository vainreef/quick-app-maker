# Fast Publish Toolchain v1 Commands

- Status: `draft`
- Command Reference: `v1`
- Runtime: `.NET 10.x`
- Target: `win-x64`
- Windows App SDK: `WINDOWS_APP_SDK_VERSION`
- WinUI templates: `WINUI_TEMPLATE_VERSION`
- winapp CLI: `WINAPP_CLI_VERSION`

这份文件承载命令细节。正式 release 前，在 Windows 11 x64 实机或 VM 上逐条验证并记录输出。

## Bootstrap and run

```powershell
dotnet new winui-navview -n APP_NAME
Set-Location APP_NAME
dotnet run
```

## Validate

```powershell
dotnet build -c Release
```

## Package for local testing

```powershell
dotnet publish -o ./publish
winapp pack ./publish --generate-cert --install-cert
```

本地开发证书、管理员确认和安装测试属于测试阶段；Store 生产签名由 Store 流程处理。

## Store submission

```powershell
winapp store publish ./*.msix --appId APP_ID
```

`APP_ID`、Partner Center 资料、身份确认、年龄评级、价格和 Store listing 由用户在人工确认点完成。

## Command record

每次 release 记录：

- 命令原文与 shell。
- 命令文档 commit。
- 标准输出、错误输出和退出码。
- 输入/输出目录。
- Windows、.NET、Windows App SDK、WinUI templates 和 `winapp` 版本。
- 是否要求管理员权限。
- 安装、启动、卸载和 Store readiness 结果。
