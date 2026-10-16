@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install the .NET 8 SDK or newer to build Desktop Mover.
    popd
    exit /b 1
)

echo Building standalone Windows Desktop Mover...
dotnet publish "WindowsDesktopMover.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "obj\standalone"
if errorlevel 1 goto :failed

copy /y "obj\standalone\WindowsDesktopMover.exe" "WindowsDesktopMover.exe" >nul
if errorlevel 1 goto :failed

echo Built: %~dp0WindowsDesktopMover.exe
popd
exit /b 0

:failed
echo ERROR: Build failed. If the executable is running, exit it from the tray and retry.
popd
exit /b 1
