# FileIndexerService - Simplified V```json
{
  "FileIndexerConfiguration": {
    "InputFolderPath": "C:\\Source\\Files",
    "ScanIntervalMinutes": 5,
    "DatabasePath": "D:\\Shared\\Database\\fileindexer.db",
    "LocalDatabasePath": "fileindexer_local.db"
  }
}
```

### Configuration Options:
- **InputFolderPath**: The folder to monitor and index
- **ScanIntervalMinutes**: How often to scan for changes (default: 5 minutes)
- **DatabasePath**: Remote/shared database file location (source and final destination)
- **LocalDatabasePath**: Local working database file path (process-local copy)## Overview
A simplified Windows Background Service that focuses solely on file indexing and database maintenance. The service monitors a specified folder and maintains a SQLite database with file metadata.

## Key Features

### ✅ **File Indexing Only**
- Monitors a single input folder recursively
- Indexes all files and maintains metadata in SQLite database
- No file copying or batch processing
- Clean, focused functionality

### ✅ **Database Copy Workflow**
- Copies database from remote path to local path at startup
- Works with local copy during processing for better performance
- Copies database back to remote path after each scan cycle
- Handles network share locks and concurrent access issues
- Automatic cleanup of local database on service shutdown

### ✅ **Smart File Tracking**
- Detects new files automatically
- Tracks file modifications by comparing timestamps
- Maintains complete history of file changes
- Stores creation date, modification date, and file size

### ✅ **Scheduled Monitoring**
- Configurable scan interval (default: 5 minutes)
- Runs as Windows Background Service
- Automatic startup with Windows
- Reliable long-term operation

### ✅ **SQLite Database**
- Lightweight, embedded database (no external dependencies)
- Efficient indexing with proper database indexes
- Simple table structure for easy querying
- File metadata preserved with timestamps

## Configuration

Simple configuration in `appsettings.json`:

```json
{
  "FileIndexerConfiguration": {
    "InputFolderPath": "C:\\Your\\Folder\\Path",
    "ScanIntervalMinutes": 5,
    "DatabasePath": "fileindexer.db"
  }
}
```

### Configuration Options:
- **InputFolderPath**: The folder to monitor and index
- **ScanIntervalMinutes**: How often to scan for changes (default: 5 minutes)
- **DatabasePath**: Location of SQLite database file

## Database Schema

**FileRecords Table:**
- `Id` - Auto-increment primary key
- `RelativePath` - Relative path from input folder
- `FileName` - File name
- `FileSizeBytes` - File size in bytes
- `CreationDate` - File creation timestamp
- `ModificationDate` - File last modified timestamp
- `IndexedDate` - When file was added to database
- `IsActive` - Boolean flag (true by default, for soft deletion/deactivation)
- `FileHash` - Optional file hash (reserved for future use)

## Use Cases

### ✅ **Perfect For:**
- File system auditing and tracking
- Change monitoring and history
- File inventory management
- Compliance and documentation
- Data archival tracking
- Simple file discovery

### ✅ **Benefits:**
- Lightweight and fast
- No file movement or modification
- Complete file history
- Easy to query database
- Minimal system impact
- Self-contained operation

## Installation

1. **As Windows Service** (Recommended):
   - Run `INSTALL-SERVICE.bat` as Administrator
   - Service starts automatically with Windows
   - Manages itself reliably

2. **Portable Mode**:
   - Run `RUN-PORTABLE.bat` for testing
   - Runs in console window
   - Good for testing and debugging

## Management

- **View Status**: Windows Services console (services.msc)
- **Check Logs**: Windows Event Viewer → Application logs
- **Query Database**: Use any SQLite viewer tool
- **Configuration**: Edit `appsettings.json` and restart service

## File Operations

### **New File Detection:**
1. Service scans input folder recursively
2. Checks if file exists in database
3. If new, adds complete metadata record
4. Logs new file count

### **Modified File Detection:**
1. Compares file modification timestamp with database
2. If different, creates new record (preserves history)
3. All new records default to `IsActive = true`
4. Maintains complete change history
5. Logs updated file count

### **File Status Management:**
- **Active Files**: `IsActive = true` (default for all new files)
- **Inactive Files**: `IsActive = false` (for soft deletion or archiving)
- **Database Methods**: `MarkFileAsActiveAsync()` and `MarkFileAsInactiveAsync()`
- **Statistics**: Tracks both active and inactive file counts

### **No File Modification:**
- Service never moves, copies, or modifies files
- Read-only operations only
- Safe for production environments
- No risk to source data

## Technical Details

- **.NET 9.0** - Modern, high-performance runtime
- **BackgroundService** - Proper Windows service pattern
- **Microsoft.Data.Sqlite** - Official SQLite provider
- **Async/Await** - Non-blocking I/O operations
- **Structured Logging** - Comprehensive operation logging
- **Self-contained** - No external dependencies required

## Deployment Package

**Package Contents:**
- `FileIndexerService.exe` - Main executable
- `appsettings.template.json` - Configuration template
- `INSTALL-SERVICE.bat` - Service installation script
- `UNINSTALL-SERVICE.bat` - Service removal script
- `RUN-PORTABLE.bat` - Portable mode launcher
- All required .NET runtime files

**Package Size:** ~35 MB (self-contained, no .NET installation required)

## Service Workflow

### **Startup Process:**
1. Copy database from `DatabasePath` to `LocalDatabasePath`
2. Initialize database connection to local copy
3. Begin monitoring input folder

### **Each Scan Cycle:**
1. Scan input folder for file changes
2. Update local database with new/modified files
3. Copy updated database back to remote `DatabasePath`
4. Wait for next scan interval

### **Shutdown Process:**
1. Dispose database connection
2. Final copy from local to remote database
3. Clean up local database file

## Version History

- **v1.x** - Complex batch processing with file copying
- **v2.0** - Simplified to indexing only, much cleaner design
- **v2.1** - Added IsActive column for soft deletion and file status management
- **v2.2** - Added database copy workflow for shared database scenarios

---

*The simplified FileIndexerService provides a focused, reliable solution for file system monitoring and indexing without the complexity of file processing workflows.*