# SharedToLocalDriveService

A Windows Service that synchronizes files from a shared network folder to a local folder, running under a dedicated service account with configurable sync intervals.

## Features

- **Automated File Synchronization**: Continuously monitors and syncs files from shared folder to local folder
- **Hostname-based Organization**: Creates and monitors a subfolder named after the current machine's hostname
- **Delete Operations**: Supports file deletion through `.delete` files
- **Timezone Awareness**: Properly handles file timestamps across different timezones
- **Service Account**: Runs under a dedicated `DriveSyncUser` account with appropriate permissions
- **Configurable Interval**: Sync interval can be configured (default: 5 minutes)
- **Comprehensive Logging**: Logs to Windows Event Log and console

## Architecture

### Components

1. **Worker**: Background service that runs the sync process at regular intervals
2. **FileSyncService**: Core service that handles file operations and synchronization logic
3. **Configuration**: Manages service settings through appsettings.json
4. **Models**: Defines data structures for file change tracking

### Sync Process

1. Creates a subfolder named after the hostname in the shared folder path
2. Monitors the `SharedFolderPath\{HostName}` directory
3. For each file found:
   - **Regular files**: Copies to local folder maintaining directory structure
   - **Files ending with `.delete`**: Deletes corresponding file from local folder and removes the `.delete` file
4. Preserves file attributes and timestamps
5. Only copies files that are newer or different from existing local files

## Installation

### Prerequisites

- Windows 10/11 or Windows Server 2016+
- .NET 8.0 Runtime
- Administrator privileges for installation
- PowerShell 5.1 or later

### Step 1: Build the Application

```powershell
# Navigate to the project directory
cd "d:\Users\singh\source\repos\DriveSync\SharedToLocalDriveService"

# Build the application
dotnet build --configuration Release

# Publish for deployment
dotnet publish --configuration Release --output ".\bin\Release\publish"
```

### Step 2: Set Up Service User and Permissions

Run PowerShell as Administrator and execute:

```powershell
.\Scripts\Setup-DriveSyncUser.ps1 -SharedFolderPath "\\server\shared" -LocalFolderPath "C:\LocalSync"
```

This script will:
- Create the `DriveSyncUser` account with a generated password
- Grant "Log on as a service" rights
- Set up folder permissions for both shared and local paths
- Create directories if they don't exist

### Step 3: Install the Windows Service

```powershell
.\Scripts\Install-Service.ps1 -ServicePath ".\bin\Release\publish" -SharedFolderPath "\\server\shared" -LocalFolderPath "C:\LocalSync" -Password "UserPasswordFromStep2"
```

Parameters:
- `ServicePath`: Path to the published application
- `SharedFolderPath`: Network path to the shared folder
- `LocalFolderPath`: Local path where files will be synced
- `Password`: Password for the DriveSyncUser account
- `SyncIntervalMinutes`: (Optional) Sync interval in minutes (default: 5)

## Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "EventLog": {
      "LogLevel": {
        "Default": "Information"
      }
    }
  },
  "ServiceConfiguration": {
    "SharedFolderPath": "\\\\server\\shared",
    "LocalFolderPath": "C:\\LocalSync",
    "SyncIntervalMinutes": 5,
    "ServiceUser": "DriveSyncUser"
  }
}
```

### Configuration Parameters

- **SharedFolderPath**: UNC path to the shared network folder
- **LocalFolderPath**: Local directory path for synchronized files
- **SyncIntervalMinutes**: How often the sync process runs (default: 5 minutes)
- **ServiceUser**: Service account name (default: "DriveSyncUser")

## Usage

### Service Management

```powershell
# Start the service
Start-Service -Name "SharedToLocalDriveService"

# Stop the service
Stop-Service -Name "SharedToLocalDriveService"

# Check service status
Get-Service -Name "SharedToLocalDriveService"

# View service properties
sc.exe query "SharedToLocalDriveService"
```

### File Operations

#### Regular File Sync
1. Place files in `{SharedFolderPath}\{HostName}\`
2. Files will be copied to `{LocalFolderPath}\` maintaining the same directory structure
3. Existing files are only replaced if the source file is newer

#### File Deletion
1. To delete a file from the local folder, create a file with the same name but add `.delete` extension
2. Example: To delete `document.txt`, create `document.txt.delete`
3. The service will:
   - Delete `document.txt` from the local folder
   - Remove the `document.txt.delete` file from the shared folder

### Monitoring and Logs

#### Event Log
- Open Event Viewer
- Navigate to Windows Logs > Application
- Filter by Source: "SharedToLocalDriveService"

#### Log Levels
- **Information**: Normal operations, sync start/completion
- **Warning**: Non-critical issues (missing directories, etc.)
- **Error**: Critical errors that prevent operation
- **Debug**: Detailed operational information

## Troubleshooting

### Common Issues

#### Service Fails to Start
1. Check Event Log for specific error messages
2. Verify `DriveSyncUser` has correct permissions
3. Ensure shared folder path is accessible
4. Confirm appsettings.json is properly configured

#### Files Not Syncing
1. Verify shared folder path exists and is accessible
2. Check that hostname folder exists in shared path
3. Ensure `DriveSyncUser` has read access to shared folder
4. Confirm local folder path is writable

#### Permission Errors
1. Re-run the user setup script with administrator privileges
2. Manually verify folder permissions in Windows Explorer
3. Check that service is running under correct user account

### Diagnostic Commands

```powershell
# Check service account
sc.exe qc "SharedToLocalDriveService"

# Test folder access as service user
runas /user:DriveSyncUser "cmd.exe"

# View detailed service information
Get-WmiObject -Class Win32_Service -Filter "Name='SharedToLocalDriveService'"
```

## Uninstallation

```powershell
# Stop and remove service only
.\Scripts\Uninstall-Service.ps1

# Stop and remove service, also remove user account
.\Scripts\Uninstall-Service.ps1 -RemoveUser
```

## Development

### Project Structure
```
SharedToLocalDriveService/
├── Configuration/
│   └── ServiceConfiguration.cs
├── Models/
│   └── FileChangeInfo.cs
├── Services/
│   ├── IFileSyncService.cs
│   └── FileSyncService.cs
├── Scripts/
│   ├── Setup-DriveSyncUser.ps1
│   ├── Install-Service.ps1
│   └── Uninstall-Service.ps1
├── Program.cs
├── Worker.cs
├── appsettings.json
└── SharedToLocalDriveService.csproj
```

### Building and Testing

```powershell
# Build
dotnet build

# Run locally (for testing)
dotnet run

# Run tests (if any)
dotnet test

# Create release package
dotnet publish -c Release
```

## Security Considerations

- The `DriveSyncUser` account is created with minimal required privileges
- Folder permissions are set to grant access only to required directories
- Service runs with "Log on as a service" right only
- Passwords should be stored securely and rotated regularly
- Consider using Group Managed Service Accounts (gMSA) in domain environments

## License

This project is provided as-is for educational and business use.