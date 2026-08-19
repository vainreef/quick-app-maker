# Fast Publish V1 Version Lock

这份文件是 Skill release 的版本输入。版本先在 Windows 11 x64 实机或 VM 上完成 Smoke Test，再把具体值写入并提交。

| Component | Required value | Current status |
| --- | --- | --- |
| Target OS | Windows 11 priority; Windows 10 1809+ compatibility | Validate on Windows |
| Golden Template | Exact template release and checksum | `GOLDEN_TEMPLATE_VERSION` / `GOLDEN_TEMPLATE_SHA256` |
| Command Reference | Versioned command file | `references/toolchain/v1/commands.md` |
| Runtime | .NET 10.x LTS | Contract fixed; patch follows test record |
| Windows App SDK | Stable channel, exact `2.x.y` | `WINDOWS_APP_SDK_VERSION` |
| WinUI templates | Exact installed template version | `WINUI_TEMPLATE_VERSION` |
| winapp CLI | Exact installed version | `WINAPP_CLI_VERSION` |
| Target architecture | `win-x64` | V1 fixed |
| Microsoft.Data.Sqlite | Exact allowlisted package version, only when enabled | `SQLITE_VERSION` |

## Lock procedure

1. Select the Command Reference listed above and record its commit.
2. On the test Windows machine, record tool versions, OS build, architecture, template checksum and dependency list.
3. Build and run the Golden Template using the selected command file.
4. Build Release output, package with the selected packaging command, install the MSIX, and test launch/uninstall/reinstall.
5. Record the source commit, package hash and test result.
6. Replace placeholders in this file and commit the lock as part of the Skill release.
7. Treat upgrades as a separate release with a fresh package and Store readiness test.
