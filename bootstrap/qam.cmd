@echo off
setlocal EnableExtensions

set "REPO_ROOT=%~dp0.."
for %%I in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fI"
if exist "%REPO_ROOT%\node\node.exe" (
  set "WORKSPACE_ROOT=%REPO_ROOT%"
) else (
  for %%I in ("%REPO_ROOT%\..") do set "WORKSPACE_ROOT=%%~fI"
)

set "NODE_EXE=%WORKSPACE_ROOT%\node\node.exe"
set "NPM_CLI=%WORKSPACE_ROOT%\node\node_modules\npm\bin\npm-cli.js"
set "QAM=%REPO_ROOT%\bin\qam.mjs"
if not exist "%NODE_EXE%" (
  echo Portable Node was not found at "%NODE_EXE%" 1>&2
  exit /b 3
)
if not exist "%NPM_CLI%" (
  echo Bundled npm CLI was not found at "%NPM_CLI%" 1>&2
  exit /b 3
)
if not exist "%QAM%" (
  echo qam.mjs was not found at "%QAM%" 1>&2
  exit /b 2
)

set "QAM_WORKSPACE_ROOT=%WORKSPACE_ROOT%"
set "QAM_REQUIRE_PORTABLE=1"
set "PATH=%WORKSPACE_ROOT%\node;%PATH%"
set "npm_config_registry=https://registry.npmmirror.com"
set "npm_config_cache=%WORKSPACE_ROOT%\.cache\npm"
set "npm_config_userconfig=%WORKSPACE_ROOT%\.cache\npmrc"
set "npm_config_fund=false"
set "npm_config_audit=false"
set "npm_config_progress=false"
set "npm_config_prefer_offline=true"
set "ELECTRON_MIRROR=https://npmmirror.com/mirrors/electron/"
set "electron_config_cache=%WORKSPACE_ROOT%\.cache\electron"
set "PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1"
if not exist "%WORKSPACE_ROOT%\.cache\npm" mkdir "%WORKSPACE_ROOT%\.cache\npm"
if not exist "%WORKSPACE_ROOT%\.cache\electron" mkdir "%WORKSPACE_ROOT%\.cache\electron"
>"%WORKSPACE_ROOT%\.cache\npmrc" echo registry=https://registry.npmmirror.com
>>"%WORKSPACE_ROOT%\.cache\npmrc" echo fund=false
>>"%WORKSPACE_ROOT%\.cache\npmrc" echo audit=false
>>"%WORKSPACE_ROOT%\.cache\npmrc" echo progress=false
>>"%WORKSPACE_ROOT%\.cache\npmrc" echo prefer-offline=true

if not exist "%REPO_ROOT%\node_modules\.package-lock.json" goto install
if not exist "%REPO_ROOT%\node_modules\@quick-app\core\package.json" goto install
goto run

:install
echo Installing quick-app-maker dependencies with bundled npm
"%NODE_EXE%" "%NPM_CLI%" --prefix "%REPO_ROOT%" ci --ignore-scripts --prefer-offline
if errorlevel 1 exit /b %ERRORLEVEL%

:run
"%NODE_EXE%" "%QAM%" %*
exit /b %ERRORLEVEL%
