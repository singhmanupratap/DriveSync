# Uninstall-FileIndexerService.ps1
# Uninstalls the FileIndexerService Windows Service

param(
    [string]$ServiceName = "FileIndexerService"
)

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{
    Write-Error "This script must be run as Administrator"
    exit 1
}

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' not found" -ForegroundColor Yellow
    exit 0
}

try {
    Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    
    Write-Host "Removing service '$ServiceName'..." -ForegroundColor Yellow
    sc.exe delete $ServiceName
    
    # Wait a moment for the service to be fully removed
    Start-Sleep -Seconds 2
    
    # Verify removal
    $removedService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($removedService) {
        Write-Warning "Service may still exist. Please check manually."
    } else {
        Write-Host "Service '$ServiceName' successfully removed!" -ForegroundColor Green
    }
    
} catch {
    Write-Error "Failed to uninstall service: $($_.Exception.Message)"
    exit 1
}