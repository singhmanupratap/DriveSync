# Database Copy Workflow - FileIndexerService v2.2

## Overview
The database copy workflow solves common issues when working with shared databases, network storage, or environments where multiple processes might access the same database file.

## Problem Solved
- **Database Locking**: SQLite can have issues with network shares
- **Concurrent Access**: Multiple services accessing the same database
- **Performance**: Local I/O is faster than network I/O
- **Reliability**: Network interruptions don't affect ongoing operations

## Workflow

### 1. Service Startup
```
[Remote Database] ----copy----> [Local Database] ----initialize----> [Service Ready]
     DatabasePath                 LocalDatabasePath
```
- Service copies database from `DatabasePath` to `LocalDatabasePath`
- If remote database doesn't exist, creates new local database
- Initializes connection to local copy only

### 2. Each Processing Cycle
```
[File Scanning] -> [Update Local DB] -> [Copy to Remote] -> [Wait for Next Cycle]
```
- All database operations use local copy for speed and reliability
- After each scan cycle, updated database is copied back to remote location
- Remote database stays current with latest changes

### 3. Service Shutdown
```
[Dispose Connection] -> [Final Copy to Remote] -> [Cleanup Local] -> [Service Stopped]
```
- Final copy ensures no data loss
- Local database file is cleaned up
- Remote database contains all changes

## Configuration Example

```json
{
  "FileIndexerConfiguration": {
    "InputFolderPath": "E:\\",
    "ScanIntervalMinutes": 5,
    "DatabasePath": "D:\\DriveSyncService\\DriveSyncService.db",
    "LocalDatabasePath": "fileindexer_local.db"
  }
}
```

### Configuration Paths:
- **DatabasePath**: Shared/remote database location (source and destination)
- **LocalDatabasePath**: Local working copy (relative to service directory)

## Benefits

### ✅ **Performance**
- Local database I/O is much faster than network I/O
- No network latency during frequent database operations
- Scanning large folders completes faster

### ✅ **Reliability**
- Service continues working even if network share is temporarily unavailable
- No database lock conflicts with other processes
- Atomic copy operations ensure data integrity

### ✅ **Concurrent Access**
- Multiple services can work with same remote database
- Each service works with its own local copy
- Changes are synchronized during copy operations

### ✅ **Error Handling**
- Copy failures don't crash the service
- Local operations continue even if remote copy fails
- Final copy on shutdown ensures data preservation

## Logging Output

```
info: Configuration loaded - Input: E:\, Database: D:\DriveSyncService\DriveSyncService.db, LocalDatabase: fileindexer_local.db
info: Remote database does not exist, will create new local database
info: Database initialized at local path: fileindexer_local.db
info: FileIndexerService started successfully - Monitoring folder: E:\
info: Starting file indexing scan of E:\...
info: File indexing completed - New: 150, Updated: 5
info: Database statistics - Total: 1250, Active: 1245, Inactive: 5, Total size: 25.6 GB
info: Copying database from fileindexer_local.db to D:\DriveSyncService\DriveSyncService.db
info: Database copied successfully to remote path
```

## Use Cases

### **Network Shares**
- Service runs locally, database stored on network share
- Avoids SQLite network share limitations
- Better performance with large file indexing operations

### **Multiple Services**
- Multiple instances indexing different folders
- All contribute to same central database
- No database locking conflicts

### **Backup Scenarios**
- Local database acts as working copy
- Remote database serves as backup/archive
- Service shutdown ensures backup is current

### **Development/Testing**
- Easy to work with local copy during development
- Production database remains stable
- Quick testing without affecting shared database

## Error Scenarios

### **Remote Database Unavailable at Startup**
- Service creates new local database
- Continues normal operation
- Copies to remote when copy operation succeeds

### **Remote Copy Fails During Operation**
- Error logged but service continues
- Local database retains all changes
- Next cycle attempts copy again

### **Network Interruption**
- Local operations unaffected
- Copy operations resume when network available
- No data loss or service interruption