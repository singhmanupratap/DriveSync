# FileIndexerService - IsActive Column Usage Examples

## Database Queries with IsActive Column

### Show All Active Files
```sql
SELECT * FROM FileRecords WHERE IsActive = 1;
```

### Show All Inactive Files
```sql
SELECT * FROM FileRecords WHERE IsActive = 0;
```

### Get Statistics by Status
```sql
SELECT 
    COUNT(*) as Total,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as Active,
    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) as Inactive,
    SUM(FileSizeBytes) as TotalSizeBytes
FROM FileRecords;
```

### Find Recently Added Active Files
```sql
SELECT * FROM FileRecords 
WHERE IsActive = 1 
ORDER BY IndexedDate DESC 
LIMIT 10;
```

## Programmatic Usage

### Mark File as Inactive (Soft Delete)
```csharp
// Using the database class directly
var database = new FileIndexerDatabase(connectionString);
await database.MarkFileAsInactiveAsync(fileId);
```

### Mark File as Active (Restore)
```csharp
// Using the database class directly
var database = new FileIndexerDatabase(connectionString);
await database.MarkFileAsActiveAsync(fileId);
```

### Update File with Custom IsActive Status
```csharp
var fileRecord = new FileRecord
{
    RelativePath = "documents",
    FileName = "example.txt",
    FileSizeBytes = 1024,
    CreationDate = DateTime.Now,
    ModificationDate = DateTime.Now,
    IndexedDate = DateTime.Now,
    IsActive = false  // Mark as inactive from the start
};

await database.InsertFileRecordAsync(fileRecord);
```

## Use Cases for IsActive Column

### 1. **Soft Deletion**
- Mark files as inactive instead of deleting records
- Preserve historical data while hiding from active queries
- Easy recovery by setting IsActive back to true

### 2. **File Archival Tracking**
- Mark archived files as inactive
- Track when files were moved to archive storage
- Maintain metadata for archived content

### 3. **Temporary File Exclusion**
- Temporarily hide files from processing
- Quality control - mark suspicious files as inactive
- Staged approval workflows

### 4. **File Lifecycle Management**
- Track file status through different lifecycle stages
- Integration with external systems
- Compliance and audit trails

## Service Statistics Output

The service now logs enhanced statistics:
```
Database statistics - Total: 150, Active: 125, Inactive: 25, Total size: 2.5 GB
```

This shows:
- **Total**: 150 total file records
- **Active**: 125 files currently active
- **Inactive**: 25 files marked as inactive
- **Total size**: Combined size of all files (active + inactive)

## Default Behavior

- **New Files**: All newly indexed files default to `IsActive = true`
- **Existing Logic**: Existing functionality unchanged
- **Backward Compatibility**: Existing databases will be automatically updated with IsActive column
- **Migration**: Old records will default to `IsActive = true` (handled by SQL DEFAULT clause)