# .\Create-Package.ps1 -Version "" -OutputPath "D:\Users\singh\OneDrive\AppPackages\FileIndexerService" -Clean

param(
    [string]$Version = "",
    [string]$OutputPath = "D:\Users\singh\OneDrive\AppPackages\FileIndexerService",
    [switch]$Clean = $false
)

if ($Version) {
    Write-Host "Creating FileIndexerService package v$Version..." -ForegroundColor Cyan
} else {
    Write-Host "Creating FileIndexerService package..." -ForegroundColor Cyan
}

# Clean build directories if requested or if they contain package artifacts
if ($Clean -or (Test-Path ".\bin\Release\publish\Package-Output") -or (Test-Path ".\bin\Release\publish\portable-package")) {
    Write-Host "Cleaning build directories..." -ForegroundColor Yellow
    if (Test-Path ".\bin") { Remove-Item ".\bin" -Recurse -Force }
    if (Test-Path ".\obj") { Remove-Item ".\obj" -Recurse -Force }
    Write-Host "Build directories cleaned" -ForegroundColor Green
}

# Build and publish
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) { exit 1 }

dotnet publish --configuration Release --self-contained --runtime win-x64 --output ".\bin\Release\publish"
if ($LASTEXITCODE -ne 0) { exit 1 }

# Create package structure locally
$packageName = if ($Version) { "FileIndexerService$Version" } else { "FileIndexerService" }
$packagePath = ".\Package-Output\$packageName"

if (Test-Path $packagePath) { Remove-Item $packagePath -Recurse -Force }
if (-not (Test-Path ".\Package-Output")) { New-Item -ItemType Directory -Path ".\Package-Output" -Force | Out-Null }
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
New-Item -ItemType Directory -Path "$packagePath\bin" -Force | Out-Null
New-Item -ItemType Directory -Path "$packagePath\Scripts" -Force | Out-Null

# Copy files (exclude any package directories that might exist)
Write-Host "Copying executable and dependencies..." -ForegroundColor Yellow
Get-ChildItem ".\bin\Release\publish\" -File | Copy-Item -Destination "$packagePath\bin\" -Force
Get-ChildItem ".\bin\Release\publish\" -Directory | Where-Object { $_.Name -notmatch "Package-Output|portable-package|FileIndexerService-Portable" } | Copy-Item -Destination "$packagePath\bin\" -Recurse -Force
Copy-Item "Scripts\*.ps1" "$packagePath\Scripts\" -Force

# Create config template
$configTemplate = @{
    "Logging" = @{
        "LogLevel" = @{ "Default" = "Information"; "Microsoft.Hosting.Lifetime" = "Information" }
    }
    "FileIndexerConfiguration" = @{
        "InputFolderPath" = "REPLACE_WITH_YOUR_INPUT_PATH"
        "TargetFolderPath" = "REPLACE_WITH_YOUR_TARGET_PATH"
        "MaxBatchSizeMB" = 1024
        "BatchDelayMinutes" = 2
        "DatabasePath" = "fileindexer.db"
    }
}
$configTemplate | ConvertTo-Json -Depth 5 | Set-Content "$packagePath\appsettings.template.json"

# Create batch files
$installService = @'
@echo off
echo Installing FileIndexerService...
if not exist "bin\appsettings.json" copy "appsettings.template.json" "bin\appsettings.json"
powershell -ExecutionPolicy Bypass -File "Scripts\Install-FileIndexerService.ps1" -ServicePath "%~dp0bin"
pause
'@
Set-Content "$packagePath\INSTALL-SERVICE.bat" $installService

$uninstallService = @'
@echo off
echo Uninstalling FileIndexerService...
powershell -ExecutionPolicy Bypass -File "Scripts\Uninstall-FileIndexerService.ps1"
pause
'@
Set-Content "$packagePath\UNINSTALL-SERVICE.bat" $uninstallService

$runPortable = @'
@echo off
echo Running FileIndexerService in console mode...
echo Press Ctrl+C to stop the service
if not exist "bin\appsettings.json" copy "appsettings.template.json" "bin\appsettings.json"
cd bin
FileIndexerService.exe
cd ..
pause
'@
Set-Content "$packagePath\RUN-PORTABLE.bat" $runPortable

$testPortable = @'
@echo off
set TEST_INPUT=C:\temp\TestInput
set TEST_TARGET=C:\temp\TestTarget
mkdir "%TEST_INPUT%" 2>nul
mkdir "%TEST_TARGET%" 2>nul
echo Creating test files...
echo Test file 1 > "%TEST_INPUT%\test1.txt"
echo Test file 2 > "%TEST_INPUT%\test2.txt"
mkdir "%TEST_INPUT%\subfolder" 2>nul
echo Nested test file > "%TEST_INPUT%\subfolder\nested.txt"

echo {"Logging":{"LogLevel":{"Default":"Information"}},"FileIndexerConfiguration":{"InputFolderPath":"%TEST_INPUT%","TargetFolderPath":"%TEST_TARGET%","MaxBatchSizeMB":1024,"BatchDelayMinutes":1,"DatabasePath":"test_fileindexer.db"}} > "bin\appsettings.json"

echo Starting FileIndexerService test (will run for 5 minutes)...
cd bin
timeout 300 FileIndexerService.exe
cd ..
echo.
echo Test completed. Check the following:
echo - Input folder: %TEST_INPUT%
echo - Target folder: %TEST_TARGET%
echo - Database: bin\test_fileindexer.db
pause
'@
Set-Content "$packagePath\TEST-PORTABLE.bat" $testPortable

# Create README
$readme = @'
# FileIndexerService

A Windows service that recursively indexes files from an input folder and copies them in batches to a target folder.

## Features

- Recursively scans input folder for files
- Stores file metadata in SQLite database
- Processes files in batches up to 1GB
- Maintains folder hierarchy
- Waits for target folder to be empty before processing next batch
- Configurable batch delay (default: 2 minutes)

## Installation

### As Windows Service (Recommended)
1. Edit `appsettings.template.json` and configure your paths
2. Run `INSTALL-SERVICE.bat` as Administrator
3. The service will start automatically

### Portable Mode
1. Edit `appsettings.template.json` and configure your paths
2. Run `RUN-PORTABLE.bat`

## Configuration

Edit `appsettings.json` (or `appsettings.template.json` before installation):

```json
{
  "FileIndexerConfiguration": {
    "InputFolderPath": "C:\\Source\\Files",
    "TargetFolderPath": "C:\\Target\\Processing", 
    "MaxBatchSizeMB": 1024,
    "BatchDelayMinutes": 2,
    "DatabasePath": "fileindexer.db"
  }
}
```

## Management

- **Install Service**: Run `INSTALL-SERVICE.bat` as Administrator
- **Uninstall Service**: Run `UNINSTALL-SERVICE.bat` as Administrator
- **Run Portable**: Run `RUN-PORTABLE.bat`
- **Test Setup**: Run `TEST-PORTABLE.bat`

## Database

The service uses SQLite database to track:
- File paths and names
- Creation and modification dates
- Processing status
- File sizes

## Logs

- **Service Mode**: Check Windows Event Logs (Application)
- **Portable Mode**: Console output
'@
Set-Content "$packagePath\README.md" $readme

# Create ZIP locally
$zipPath = ".\Package-Output\$packageName.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $packagePath -DestinationPath $zipPath -Force

# Remove the package folder after creating ZIP
Write-Host "Cleaning up package folder..." -ForegroundColor Yellow
Remove-Item $packagePath -Recurse -Force

# Copy ZIP to OutputPath if specified and different from current location
if ($OutputPath -and $OutputPath -ne ".\Package-Output") {
    if (-not (Test-Path $OutputPath)) { New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null }
    $finalZipPath = Join-Path $OutputPath "$packageName.zip"
    Copy-Item $zipPath $finalZipPath -Force
    Write-Host "ZIP copied to: $finalZipPath" -ForegroundColor Green
    $zipPath = $finalZipPath
}

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "FileIndexerService package created: $zipPath ($sizeMB MB)" -ForegroundColor Green