# Fast Publish V1 Version Lock

这份文件是 Skill release 的版本输入。版本先在 Windows 11 x64 实机或 VM 上完成 Smoke Test，再把具体值写入并提交。

| Component | Required value | Current status |
| --- | --- | --- |
| Target OS | Windows 11 priority; Windows 10 1809+ compatibility | Validate on Windows |
| Runtime | .NET 10.x LTS | Contract fixed; patch follows test record |
| Windows App SDK | Stable channel, exact `2.x.y` | `WINDOWS_APP_SDK_VERSION` |
| WinUI templates | Exact installed template version | `WINUI_TEMPLATE_VERSION` |
| winapp CLI | Exact installed version | `WINAPP_CLI_VERSION` |
| Target architecture | `win-x64` | V1 fixed |
| Microsoft.Data.Sqlite | Exact allowlisted package version, only when enabled | `SQLITE_VERSION` |

## Lock procedure

1. On the test Windows machine, record `dotnet --info`, `dotnet list package`, `winapp --version`, `winapp --help`, OS build and architecture.
2. Build and run the Golden Template with `dotnet run`.
3. Build Release output, package with `winapp pack`, install the MSIX, and test launch/uninstall/reinstall.
4. Record the source commit, package hash and test result.
5. Replace placeholders in this file and commit the lock as part of the Skill release.
6. Treat upgrades as a separate release with a fresh package and Store readiness test.
