# Quick Start Script for SharedToLocalDriveService
# This script performs the complete setup process
# Run as Administrator

param(
    [Parameter(Mandatory=$true)]
    [string]$SharedFolderPath,
    
    [Parameter(Mandatory=$true)]
    [string]$LocalFolderPath,
    
    [int]$SyncIntervalMinutes = 5,
    [string]$Username = "DriveSyncUser",
    [string]$ServiceName = "SharedToLocalDriveService"
)

$ErrorActionPreference = "Stop"

Write-Host "=== SharedToLocalDriveService Quick Setup ===" -ForegroundColor Cyan
Write-Host "This script will:" -ForegroundColor White
Write-Host "1. Build the application" -ForegroundColor White
Write-Host "2. Create the service user account" -ForegroundColor White
Write-Host "3. Set up folder permissions" -ForegroundColor White
Write-Host "4. Install and start the Windows service" -ForegroundColor White
Write-Host ""

try {
    # Step 1: Build the application
    Write-Host "Step 1: Building the application..." -ForegroundColor Green
    
    if (-not (Test-Path "SharedToLocalDriveService.csproj")) {
        throw "SharedToLocalDriveService.csproj not found. Please run this script from the project root directory."
    }
    
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    dotnet publish --configuration Release --output ".\bin\Release\publish"
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed"
    }
    
    Write-Host "✓ Application built successfully" -ForegroundColor Green
    
    # Step 2: Set up user and permissions
    Write-Host "`nStep 2: Setting up service user and permissions..." -ForegroundColor Green
    
    & ".\Scripts\Setup-DriveSyncUser.ps1" -SharedFolderPath $SharedFolderPath -LocalFolderPath $LocalFolderPath -Username $Username
    if ($LASTEXITCODE -ne 0) {
        throw "User setup failed"
    }
    
    Write-Host "✓ Service user and permissions configured" -ForegroundColor Green
    
    # Step 3: Get password for service installation
    Write-Host "`nStep 3: Service installation..." -ForegroundColor Green
    $SecurePassword = Read-Host "Enter the password for user '$Username' (from Step 2)" -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
    $Password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    
    # Step 4: Install the service
    $ServicePath = Join-Path (Get-Location) "bin\Release\publish"
    
    & ".\Scripts\Install-Service.ps1" -ServicePath $ServicePath -SharedFolderPath $SharedFolderPath -LocalFolderPath $LocalFolderPath -Username $Username -Password $Password -SyncIntervalMinutes $SyncIntervalMinutes
    if ($LASTEXITCODE -ne 0) {
        throw "Service installation failed"
    }
    
    Write-Host "✓ Service installed and started successfully" -ForegroundColor Green
    
    # Final status check
    Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
    
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq "Running") {
        Write-Host "✓ Service is running" -ForegroundColor Green
        
        Write-Host "`nConfiguration Summary:" -ForegroundColor White
        Write-Host "- Service Name: $ServiceName" -ForegroundColor Gray
        Write-Host "- Service User: $Username" -ForegroundColor Gray
        Write-Host "- Shared Folder: $SharedFolderPath" -ForegroundColor Gray
        Write-Host "- Local Folder: $LocalFolderPath" -ForegroundColor Gray
        Write-Host "- Sync Interval: $SyncIntervalMinutes minutes" -ForegroundColor Gray
        Write-Host "- Host Folder: $SharedFolderPath\$(hostname)" -ForegroundColor Gray
        
        Write-Host "`nNext Steps:" -ForegroundColor Cyan
        Write-Host "1. Place files in: $SharedFolderPath\$(hostname)" -ForegroundColor White
        Write-Host "2. Files will be synced to: $LocalFolderPath" -ForegroundColor White
        Write-Host "3. Monitor logs in Windows Event Viewer (Application log, Source: SharedToLocalDriveService)" -ForegroundColor White
        Write-Host "4. Use Services.msc to manage the service" -ForegroundColor White
        
    } else {
        Write-Warning "Service was installed but is not running. Check Event Log for details."
    }
    
} catch {
    Write-Error "Setup failed: $_"
    Write-Host "`nTroubleshooting:" -ForegroundColor Yellow
    Write-Host "- Ensure you're running PowerShell as Administrator" -ForegroundColor White
    Write-Host "- Check that .NET 8.0 Runtime is installed" -ForegroundColor White
    Write-Host "- Verify network access to shared folder path" -ForegroundColor White
    Write-Host "- Check Windows Event Log for detailed error messages" -ForegroundColor White
    exit 1
}

Write-Host "`nSetup completed successfully! 🎉" -ForegroundColor Green