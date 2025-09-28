@echo off
echo Building SharedToLocalDriveService...
echo.

REM Build the project
dotnet build --configuration Release
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)

REM Publish the project
dotnet publish --configuration Release --output ".\bin\Release\publish"
if %errorlevel% neq 0 (
    echo Publish failed!
    pause
    exit /b 1
)

echo.
echo Build completed successfully!
echo Published to: .\bin\Release\publish
echo.
echo Next steps:
echo 1. Run PowerShell as Administrator
echo 2. Execute: .\Scripts\Quick-Setup.ps1 -SharedFolderPath "\\server\shared" -LocalFolderPath "C:\LocalSync"
echo.
pause