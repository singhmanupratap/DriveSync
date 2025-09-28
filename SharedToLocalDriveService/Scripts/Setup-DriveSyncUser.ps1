# PowerShell script to create DriveSyncUser and set up permissions
# Run this script as Administrator

param(
    [Parameter(Mandatory=$true)]
    [string]$SharedFolderPath,
    
    [Parameter(Mandatory=$true)]
    [string]$LocalFolderPath,
    
    [string]$Username = "DriveSyncUser",
    
    [string]$Password = $null
)

# Function to generate a random password
function Generate-RandomPassword {
    param([int]$Length = 16)
    
    $chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*"
    $password = ""
    for ($i = 0; $i -lt $Length; $i++) {
        $password += $chars[(Get-Random -Maximum $chars.Length)]
    }
    return $password
}

try {
    Write-Host "Setting up DriveSyncUser account and permissions..." -ForegroundColor Green
    
    # Generate password if not provided
    if ([string]::IsNullOrEmpty($Password)) {
        $Password = Generate-RandomPassword
        Write-Host "Generated password for user '$Username': $Password" -ForegroundColor Yellow
        Write-Host "Please save this password securely!" -ForegroundColor Red
    }
    
    # Convert password to secure string
    $SecurePassword = ConvertTo-SecureString $Password -AsPlainText -Force
    
    # Check if user already exists
    $existingUser = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
    
    if ($existingUser) {
        Write-Host "User '$Username' already exists. Updating password..." -ForegroundColor Yellow
        Set-LocalUser -Name $Username -Password $SecurePassword
    } else {
        Write-Host "Creating user '$Username'..." -ForegroundColor Green
        New-LocalUser -Name $Username -Password $SecurePassword -FullName "Drive Sync Service User" -Description "Service account for SharedToLocalDriveService" -PasswordNeverExpires
    }
    
    # Add user to required groups
    Write-Host "Adding user to required groups..." -ForegroundColor Green
    
    # Add to "Log on as a service" right
    $userSID = (Get-LocalUser -Name $Username).SID.Value
    
    # Grant "Log on as a service" right using secedit
    $tempFile = [System.IO.Path]::GetTempFileName()
    $configFile = [System.IO.Path]::GetTempFileName()
    
    secedit /export /cfg $tempFile
    $content = Get-Content $tempFile
    
    $serviceLogonRight = "SeServiceLogonRight"
    $found = $false
    
    for ($i = 0; $i -lt $content.Length; $i++) {
        if ($content[$i] -match "^$serviceLogonRight = ") {
            if ($content[$i] -notmatch $userSID) {
                $content[$i] = $content[$i] + ",*$userSID"
            }
            $found = $true
            break
        }
    }
    
    if (-not $found) {
        $content += "$serviceLogonRight = *$userSID"
    }
    
    $content | Set-Content $configFile
    secedit /configure /db secedit.sdb /cfg $configFile
    
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    Remove-Item $configFile -Force -ErrorAction SilentlyContinue
    Remove-Item "secedit.sdb" -Force -ErrorAction SilentlyContinue
    
    # Set up folder permissions
    Write-Host "Setting up folder permissions..." -ForegroundColor Green
    
    # Ensure SharedFolderPath exists and set permissions
    if (-not (Test-Path $SharedFolderPath)) {
        Write-Host "Creating shared folder: $SharedFolderPath" -ForegroundColor Green
        New-Item -ItemType Directory -Path $SharedFolderPath -Force | Out-Null
    }
    
    # Grant Full Control to SharedFolderPath
    $acl = Get-Acl $SharedFolderPath
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($Username, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($accessRule)
    Set-Acl $SharedFolderPath $acl
    Write-Host "Granted Full Control permissions to '$Username' on '$SharedFolderPath'" -ForegroundColor Green
    
    # Ensure LocalFolderPath exists and set permissions
    if (-not (Test-Path $LocalFolderPath)) {
        Write-Host "Creating local folder: $LocalFolderPath" -ForegroundColor Green
        New-Item -ItemType Directory -Path $LocalFolderPath -Force | Out-Null
    }
    
    # Grant Full Control to LocalFolderPath
    $acl = Get-Acl $LocalFolderPath
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($Username, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($accessRule)
    Set-Acl $LocalFolderPath $acl
    Write-Host "Granted Full Control permissions to '$Username' on '$LocalFolderPath'" -ForegroundColor Green
    
    Write-Host "`nSetup completed successfully!" -ForegroundColor Green
    Write-Host "User '$Username' has been created/updated with the following permissions:" -ForegroundColor White
    Write-Host "- Log on as a service right" -ForegroundColor White
    Write-Host "- Full Control on '$SharedFolderPath'" -ForegroundColor White
    Write-Host "- Full Control on '$LocalFolderPath'" -ForegroundColor White
    
    if ([string]::IsNullOrEmpty($Password)) {
        Write-Host "`nIMPORTANT: Save this password: $Password" -ForegroundColor Red
    }
    
} catch {
    Write-Error "Error setting up user and permissions: $_"
    exit 1
}