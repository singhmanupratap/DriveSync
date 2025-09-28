# PowerShell script to install SharedToLocalDriveService as Windows Service
# Run this script as Administrator

param(
    [Parameter(Mandatory=$true)]
    [string]$ServicePath,
    
    [Parameter(Mandatory=$true)]
    [string]$SharedFolderPath,
    
    [Parameter(Mandatory=$true)]
    [string]$LocalFolderPath,
    
    [string]$ServiceName = "SharedToLocalDriveService",
    [string]$DisplayName = "Shared to Local Drive Sync Service",
    [string]$Description = "Synchronizes files from shared folder to local folder",
    [string]$Username = "DriveSyncUser",
    [string]$Password = $null,
    [int]$SyncIntervalMinutes = 5
)

try {
    Write-Host "Installing $ServiceName..." -ForegroundColor Green
    
    # Verify service executable exists
    $serviceExe = Join-Path $ServicePath "SharedToLocalDriveService.exe"
    if (-not (Test-Path $serviceExe)) {
        throw "Service executable not found at: $serviceExe"
    }
    
    # Check if service already exists
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "Service '$ServiceName' already exists. Stopping and removing..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName
        Start-Sleep -Seconds 2
    }
    
    # Prompt for password if not provided
    if ([string]::IsNullOrEmpty($Password)) {
        $SecurePassword = Read-Host "Enter password for user '$Username'" -AsSecureString
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
        $Password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    }
    
    # Create the service
    Write-Host "Creating Windows Service..." -ForegroundColor Green
    $result = & sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto obj= ".\$Username" password= "$Password" DisplayName= "$DisplayName"
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create service. Error: $result"
    }
    
    # Set service description
    & sc.exe description $ServiceName "$Description"
    
    # Configure service recovery options
    Write-Host "Configuring service recovery options..." -ForegroundColor Green
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    # Update appsettings.json with provided paths
    $configPath = Join-Path $ServicePath "appsettings.json"
    if (Test-Path $configPath) {
        Write-Host "Updating service configuration..." -ForegroundColor Green
        
        $config = Get-Content $configPath | ConvertFrom-Json
        $config.ServiceConfiguration.SharedFolderPath = $SharedFolderPath
        $config.ServiceConfiguration.LocalFolderPath = $LocalFolderPath
        $config.ServiceConfiguration.SyncIntervalMinutes = $SyncIntervalMinutes
        $config.ServiceConfiguration.ServiceUser = $Username
        
        $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
        Write-Host "Configuration updated successfully" -ForegroundColor Green
    }
    
    # Start the service
    Write-Host "Starting service..." -ForegroundColor Green
    Start-Service -Name $ServiceName
    
    # Wait a moment and check service status
    Start-Sleep -Seconds 3
    $service = Get-Service -Name $ServiceName
    
    if ($service.Status -eq "Running") {
        Write-Host "`nService installed and started successfully!" -ForegroundColor Green
        Write-Host "Service Name: $ServiceName" -ForegroundColor White
        Write-Host "Display Name: $DisplayName" -ForegroundColor White
        Write-Host "Status: $($service.Status)" -ForegroundColor White
        Write-Host "Shared Folder: $SharedFolderPath" -ForegroundColor White
        Write-Host "Local Folder: $LocalFolderPath" -ForegroundColor White
        Write-Host "Sync Interval: $SyncIntervalMinutes minutes" -ForegroundColor White
        Write-Host "Service User: $Username" -ForegroundColor White
        
        Write-Host "`nYou can manage this service using:" -ForegroundColor Cyan
        Write-Host "- Services.msc (Windows Services Manager)" -ForegroundColor White
        Write-Host "- sc.exe commands (sc start/stop/query $ServiceName)" -ForegroundColor White
        Write-Host "- PowerShell commands (Start-Service/Stop-Service -Name $ServiceName)" -ForegroundColor White
        
    } else {
        Write-Warning "Service was created but failed to start. Status: $($service.Status)"
        Write-Host "Check the Event Log for error details." -ForegroundColor Yellow
    }
    
} catch {
    Write-Error "Error installing service: $_"
    exit 1
}