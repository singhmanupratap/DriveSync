# Install-FileIndexerService.ps1
# Installs the FileIndexerService as a Windows Service

param(
    [Parameter(Mandatory=$true)]
    [string]$ServicePath,
    
    [string]$ServiceName = "FileIndexerService",
    [string]$DisplayName = "File Indexer Service",
    [string]$Description = "Service that recursively indexes files from input folder and copies them in batches to target folder",
    [string]$StartupType = "Automatic"
)

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{
    Write-Error "This script must be run as Administrator"
    exit 1
}

# Check if service executable exists
$executablePath = Join-Path $ServicePath "FileIndexerService.exe"
if (-not (Test-Path $executablePath)) {
    Write-Error "Service executable not found at: $executablePath"
    exit 1
}

# Stop and remove existing service if it exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    
    Write-Host "Removing existing service..." -ForegroundColor Yellow
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

try {
    # Install the service
    Write-Host "Installing service '$DisplayName'..." -ForegroundColor Green
    New-Service -Name $ServiceName -BinaryPathName $executablePath -DisplayName $DisplayName -Description $Description -StartupType $StartupType
    
    Write-Host "Service installed successfully!" -ForegroundColor Green
    Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
    Write-Host "Executable Path: $executablePath" -ForegroundColor Cyan
    Write-Host "Startup Type: $StartupType" -ForegroundColor Cyan
    
    # Start the service
    Write-Host "Starting service..." -ForegroundColor Green
    Start-Service -Name $ServiceName
    
    # Display service status
    $service = Get-Service -Name $ServiceName
    Write-Host "Service Status: $($service.Status)" -ForegroundColor Cyan
    
    Write-Host "`nService installed and started successfully!" -ForegroundColor Green
    Write-Host "You can manage the service using:" -ForegroundColor Yellow
    Write-Host "  - Services.msc" -ForegroundColor White
    Write-Host "  - sc.exe start $ServiceName" -ForegroundColor White
    Write-Host "  - sc.exe stop $ServiceName" -ForegroundColor White
    Write-Host "  - Get-Service -Name $ServiceName" -ForegroundColor White
    
} catch {
    Write-Error "Failed to install service: $($_.Exception.Message)"
    exit 1
}