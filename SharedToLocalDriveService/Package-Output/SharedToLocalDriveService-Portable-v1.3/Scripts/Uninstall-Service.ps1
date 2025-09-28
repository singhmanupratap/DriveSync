# PowerShell script to uninstall SharedToLocalDriveService
# Run this script as Administrator

param(
    [string]$ServiceName = "SharedToLocalDriveService",
    [switch]$RemoveUser = $false,
    [string]$Username = "DriveSyncUser"
)

try {
    Write-Host "Uninstalling $ServiceName..." -ForegroundColor Green
    
    # Check if service exists
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if ($service) {
        # Stop the service if it's running
        if ($service.Status -eq "Running") {
            Write-Host "Stopping service..." -ForegroundColor Yellow
            Stop-Service -Name $ServiceName -Force
            
            # Wait for service to stop
            $timeout = 30
            $elapsed = 0
            do {
                Start-Sleep -Seconds 1
                $elapsed++
                $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
            } while ($service.Status -eq "Running" -and $elapsed -lt $timeout)
        }
        
        # Remove the service
        Write-Host "Removing service..." -ForegroundColor Green
        & sc.exe delete $ServiceName
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Service '$ServiceName' removed successfully" -ForegroundColor Green
        } else {
            Write-Warning "Failed to remove service. It may need to be removed manually."
        }
    } else {
        Write-Host "Service '$ServiceName' not found" -ForegroundColor Yellow
    }
    
    # Remove user if requested
    if ($RemoveUser) {
        Write-Host "Removing user account '$Username'..." -ForegroundColor Green
        
        $existingUser = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
        if ($existingUser) {
            Remove-LocalUser -Name $Username
            Write-Host "User '$Username' removed successfully" -ForegroundColor Green
        } else {
            Write-Host "User '$Username' not found" -ForegroundColor Yellow
        }
    }
    
    Write-Host "`nUninstallation completed!" -ForegroundColor Green
    
    if (-not $RemoveUser) {
        Write-Host "Note: User account '$Username' was not removed." -ForegroundColor Cyan
        Write-Host "Use -RemoveUser switch to remove the user account as well." -ForegroundColor Cyan
    }
    
} catch {
    Write-Error "Error during uninstallation: $_"
    exit 1
}