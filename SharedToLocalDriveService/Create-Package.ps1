# .\Create-Package.ps1 -Version "" -OutputPath "D:\Users\singh\OneDrive\AppPackages\SharedToLocalDriveService" -Clean

param(
    [string]$Version = "",
    [string]$OutputPath = "D:\Users\singh\OneDrive\AppPackages\SharedToLocalDriveService",
    [switch]$Clean = $false
)

if ($Version) {
    Write-Host "Creating package v$Version..." -ForegroundColor Cyan
} else {
    Write-Host "Creating package..." -ForegroundColor Cyan
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
$packageName = if ($Version) { "SharedToLocalDriveService$Version" } else { "SharedToLocalDriveService" }
$packagePath = ".\Package-Output\$packageName"

if (Test-Path $packagePath) { Remove-Item $packagePath -Recurse -Force }
if (-not (Test-Path ".\Package-Output")) { New-Item -ItemType Directory -Path ".\Package-Output" -Force | Out-Null }
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
New-Item -ItemType Directory -Path "$packagePath\bin" -Force | Out-Null
New-Item -ItemType Directory -Path "$packagePath\Scripts" -Force | Out-Null

# Copy files (exclude any package directories that might exist)
Write-Host "Copying executable and dependencies..." -ForegroundColor Yellow
Get-ChildItem ".\bin\Release\publish\" -File | Copy-Item -Destination "$packagePath\bin\" -Force
Get-ChildItem ".\bin\Release\publish\" -Directory | Where-Object { $_.Name -notmatch "Package-Output|portable-package|SharedToLocalDriveService-Portable" } | Copy-Item -Destination "$packagePath\bin\" -Recurse -Force
Copy-Item "Scripts\*.ps1" "$packagePath\Scripts\" -Force

# Create config template
$configTemplate = @{
    "Logging" = @{
        "LogLevel" = @{ "Default" = "Information" }
    }
    "ServiceConfiguration" = @{
        "SharedFolderPath" = "REPLACE_WITH_YOUR_SHARED_PATH"
        "LocalFolderPath" = "REPLACE_WITH_YOUR_LOCAL_PATH"
        "SyncIntervalMinutes" = 5
    }
}
$configTemplate | ConvertTo-Json -Depth 5 | Set-Content "$packagePath\appsettings.template.json"

# Create batch files
$runPortable = @'
@echo off
if not exist "bin\appsettings.json" copy "appsettings.template.json" "bin\appsettings.json"
cd bin
SharedToLocalDriveService.exe
cd ..
pause
'@
Set-Content "$packagePath\RUN-PORTABLE.bat" $runPortable

$testPortable = @'
@echo off
set TEST_SHARED=C:\temp\TestShared
set TEST_LOCAL=C:\temp\TestLocal
mkdir "%TEST_SHARED%\%COMPUTERNAME%" 2>nul
mkdir "%TEST_LOCAL%" 2>nul
echo {"ServiceConfiguration":{"SharedFolderPath":"%TEST_SHARED%","LocalFolderPath":"%TEST_LOCAL%","SyncIntervalMinutes":1}} > "bin\appsettings.json"
echo Test file > "%TEST_SHARED%\%COMPUTERNAME%\test.txt"
cd bin
timeout 30 SharedToLocalDriveService.exe
cd ..
if exist "%TEST_LOCAL%\test.txt" echo SUCCESS! else echo FAILED
pause
'@
Set-Content "$packagePath\TEST-PORTABLE.bat" $testPortable

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
Write-Host "Package created: $zipPath ($sizeMB MB)" -ForegroundColor Green