@echo off
setlocal

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
set "SELF_CONTAINED=%~2"
if "%SELF_CONTAINED%"=="" set "SELF_CONTAINED=false"
set "WORKSPACE_PATH=%~dp0.."
set "SOLUTION_PATH=%WORKSPACE_PATH%\WindowsDeskNote.sln"
set "PUBLISH_PATH=%WORKSPACE_PATH%\artifacts\publish\win-x64"

dotnet restore "%SOLUTION_PATH%"
if errorlevel 1 exit /b %errorlevel%

dotnet test "%SOLUTION_PATH%" --configuration "%CONFIGURATION%" --no-restore
if errorlevel 1 exit /b %errorlevel%

dotnet publish "%WORKSPACE_PATH%\src\DeskNote.App\DeskNote.App.csproj" ^
  --configuration "%CONFIGURATION%" ^
  --runtime win-x64 ^
  --self-contained "%SELF_CONTAINED%" ^
  --output "%PUBLISH_PATH%"
exit /b %errorlevel%
