using Microsoft.Data.Sqlite;
using Shared.Models;

namespace Shared.Data;

public class FileIndexerDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed = false;

    public FileIndexerDatabase(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var createTableCommand = _connection.CreateCommand();
        createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS FileRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RelativePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                CreationDate TEXT NOT NULL,
                ModificationDate TEXT NOT NULL,
                IndexedDate TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                FileHash TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS FileRequests (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileId INTEGER NOT NULL,
                RequestedByHost TEXT NOT NULL,
                RequestedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (FileId) REFERENCES FileRecords (Id)
            );

            CREATE INDEX IF NOT EXISTS idx_relativepath ON FileRecords(RelativePath);
            CREATE INDEX IF NOT EXISTS idx_filename ON FileRecords(FileName);
            CREATE INDEX IF NOT EXISTS idx_isactive ON FileRecords(IsActive);
            CREATE INDEX IF NOT EXISTS idx_filerequests_fileid ON FileRequests(FileId);
            CREATE INDEX IF NOT EXISTS idx_filerequests_isactive ON FileRequests(IsActive);
        ";
        createTableCommand.ExecuteNonQuery();
    }

    public async Task<bool> FileExistsAsync(string relativePath, string fileName)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FileRecords WHERE RelativePath = @relativePath AND FileName = @fileName";
        command.Parameters.AddWithValue("@relativePath", relativePath);
        command.Parameters.AddWithValue("@fileName", fileName);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<FileRecord?> GetFileRecordAsync(string relativePath, string fileName)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, RelativePath, FileName, FileSizeBytes, CreationDate, ModificationDate, 
                   IndexedDate, IsActive, FileHash 
            FROM FileRecords 
            WHERE RelativePath = @relativePath AND FileName = @fileName";
        command.Parameters.AddWithValue("@relativePath", relativePath);
        command.Parameters.AddWithValue("@fileName", fileName);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                CreationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationDate"))),
                ModificationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModificationDate"))),
                IndexedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("IndexedDate"))),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            };
        }
        return null;
    }

    public async Task<FileRecord?> GetFileRecordByIdAsync(int id)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, RelativePath, FileName, FileSizeBytes, CreationDate, ModificationDate, 
                   IndexedDate, IsActive, FileHash 
            FROM FileRecords 
            WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                CreationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationDate"))),
                ModificationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModificationDate"))),
                IndexedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("IndexedDate"))),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            };
        }
        return null;
    }

    public async Task<(List<FileRecord> Records, int TotalCount)> GetFileRecordsPagedAsync(
        int page = 1, 
        int pageSize = 50, 
        string? searchTerm = null, 
        string? sortBy = "IndexedDate", 
        bool sortAscending = false,
        bool? isActive = null)
    {
        var whereClauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        // Filter by active status if specified
        if (isActive.HasValue)
        {
            whereClauses.Add("IsActive = @isActive");
            parameters.Add(("@isActive", isActive.Value ? 1 : 0));
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            whereClauses.Add("(FileName LIKE @searchTerm OR RelativePath LIKE @searchTerm)");
            parameters.Add(("@searchTerm", $"%{searchTerm}%"));
        }

        var whereClause = whereClauses.Any() ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";
        
        // Get total count
        var countCommand = _connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM FileRecords {whereClause}";
        foreach (var param in parameters)
        {
            countCommand.Parameters.AddWithValue(param.Name, param.Value);
        }
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        // Get paged records
        var sortColumn = sortBy switch
        {
            "FileName" => "FileName",
            "RelativePath" => "RelativePath",
            "FileSizeBytes" => "FileSizeBytes",
            "CreationDate" => "CreationDate",
            "ModificationDate" => "ModificationDate",
            "IsActive" => "IsActive",
            _ => "IndexedDate"
        };

        var sortDirection = sortAscending ? "ASC" : "DESC";
        var offset = (page - 1) * pageSize;

        var dataCommand = _connection.CreateCommand();
        dataCommand.CommandText = $@"
            SELECT Id, RelativePath, FileName, FileSizeBytes, CreationDate, ModificationDate, 
                   IndexedDate, IsActive, FileHash 
            FROM FileRecords 
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}
            LIMIT @pageSize OFFSET @offset";
        
        foreach (var param in parameters)
        {
            dataCommand.Parameters.AddWithValue(param.Name, param.Value);
        }
        dataCommand.Parameters.AddWithValue("@pageSize", pageSize);
        dataCommand.Parameters.AddWithValue("@offset", offset);

        var records = new List<FileRecord>();
        using var reader = await dataCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new FileRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                CreationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationDate"))),
                ModificationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModificationDate"))),
                IndexedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("IndexedDate"))),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            });
        }

        return (records, totalCount);
    }

    public async Task<bool> InsertFileRecordAsync(FileRecord fileRecord)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO FileRecords (RelativePath, FileName, FileSizeBytes, CreationDate, 
                                   ModificationDate, IndexedDate, IsActive, FileHash)
            VALUES (@relativePath, @fileName, @fileSizeBytes, @creationDate, 
                    @modificationDate, @indexedDate, @isActive, @fileHash)";
        
        command.Parameters.AddWithValue("@relativePath", fileRecord.RelativePath);
        command.Parameters.AddWithValue("@fileName", fileRecord.FileName);
        command.Parameters.AddWithValue("@fileSizeBytes", fileRecord.FileSizeBytes);
        command.Parameters.AddWithValue("@creationDate", fileRecord.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@modificationDate", fileRecord.ModificationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@indexedDate", fileRecord.IndexedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@isActive", fileRecord.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@fileHash", fileRecord.FileHash ?? (object)DBNull.Value);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateFileRecordAsync(int id, FileRecord fileRecord)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileRecords 
            SET FileSizeBytes = @fileSizeBytes, 
                ModificationDate = @modificationDate, 
                IndexedDate = @indexedDate,
                IsActive = @isActive,
                FileHash = @fileHash
            WHERE Id = @id";
        
        command.Parameters.AddWithValue("@fileSizeBytes", fileRecord.FileSizeBytes);
        command.Parameters.AddWithValue("@modificationDate", fileRecord.ModificationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@indexedDate", fileRecord.IndexedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@isActive", fileRecord.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@fileHash", fileRecord.FileHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> MarkFileAsInactiveAsync(int id)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileRecords 
            SET IsActive = 0
            WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> MarkFileAsActiveAsync(int id)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileRecords 
            SET IsActive = 1
            WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> MarkFileAsInactiveWithTimestampAsync(int id)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileRecords 
            SET IsActive = 0,
                ModificationDate = @modificationDate
            WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@modificationDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<(int total, int active, int inactive, long totalSizeBytes)> GetStatisticsAsync()
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as Active,
                SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) as Inactive,
                SUM(FileSizeBytes) as TotalSizeBytes
            FROM FileRecords";

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetInt32(reader.GetOrdinal("Total")),
                reader.GetInt32(reader.GetOrdinal("Active")),
                reader.GetInt32(reader.GetOrdinal("Inactive")),
                reader.GetInt64(reader.GetOrdinal("TotalSizeBytes"))
            );
        }

        return (0, 0, 0, 0);
    }

    // Methods for file deletion service
    public async Task<List<FileRecord>> GetInactiveFilesForDeletionAsync(DateTime? lastProcessedDate = null, int batchSize = 100)
    {
        var command = _connection.CreateCommand();
        
        string whereClause = "WHERE IsActive = 0";
        if (lastProcessedDate.HasValue)
        {
            whereClause += " AND ModificationDate > @lastProcessedDate";
        }
        
        command.CommandText = $@"
            SELECT Id, RelativePath, FileName, FileSizeBytes, CreationDate, ModificationDate, 
                   IndexedDate, IsActive, FileHash
            FROM FileRecords 
            {whereClause}
            ORDER BY ModificationDate ASC
            LIMIT @batchSize";

        if (lastProcessedDate.HasValue)
        {
            command.Parameters.AddWithValue("@lastProcessedDate", lastProcessedDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        command.Parameters.AddWithValue("@batchSize", batchSize);

        var files = new List<FileRecord>();
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            files.Add(new FileRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                CreationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationDate"))),
                ModificationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModificationDate"))),
                IndexedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("IndexedDate"))),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            });
        }

        return files;
    }

    public async Task<DateTime?> GetLastProcessedModificationDateAsync()
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT MAX(ModificationDate) as LastModificationDate
            FROM FileRecords 
            WHERE IsActive = 0";

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync() && !reader.IsDBNull(reader.GetOrdinal("LastModificationDate")))
        {
            return DateTime.Parse(reader.GetString(reader.GetOrdinal("LastModificationDate")));
        }

        return null;
    }

    public async Task<bool> DeleteFileRecordAsync(int id)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM FileRecords WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> InsertRestoredFileRecordAsync(FileRecord inactiveRecord, DateTime newModificationDate, long newFileSizeBytes)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO FileRecords (RelativePath, FileName, FileSizeBytes, CreationDate, 
                                   ModificationDate, IndexedDate, IsActive, FileHash)
            VALUES (@relativePath, @fileName, @fileSizeBytes, @creationDate, 
                    @modificationDate, @indexedDate, @isActive, @fileHash)";
        
        command.Parameters.AddWithValue("@relativePath", inactiveRecord.RelativePath);
        command.Parameters.AddWithValue("@fileName", inactiveRecord.FileName);
        command.Parameters.AddWithValue("@fileSizeBytes", newFileSizeBytes);
        command.Parameters.AddWithValue("@creationDate", inactiveRecord.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@modificationDate", newModificationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@indexedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@isActive", 1); // Always active for restored files
        command.Parameters.AddWithValue("@fileHash", (object)DBNull.Value); // Will be calculated later by indexer

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    // FileRequest methods
    public async Task<bool> CreateFileRequestAsync(int fileId, string requestedByHost)
    {
        // First check if an active request already exists
        var existingRequest = await GetActiveFileRequestAsync(fileId, requestedByHost);
        if (existingRequest != null)
        {
            return false; // Request already exists
        }

        var query = @"
            INSERT INTO FileRequests (FileId, RequestedByHost, RequestedAt, IsActive)
            VALUES (@fileId, @requestedByHost, @requestedAt, @isActive)";

        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@fileId", fileId);
        command.Parameters.AddWithValue("@requestedByHost", requestedByHost);
        command.Parameters.AddWithValue("@requestedAt", DateTime.Now);
        command.Parameters.AddWithValue("@isActive", 1);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<FileRequest?> GetActiveFileRequestAsync(int fileId, string requestedByHost)
    {
        var query = @"
            SELECT Id, FileId, RequestedByHost, RequestedAt, IsActive
            FROM FileRequests 
            WHERE FileId = @fileId AND RequestedByHost = @requestedByHost AND IsActive = 1
            LIMIT 1";

        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@fileId", fileId);
        command.Parameters.AddWithValue("@requestedByHost", requestedByHost);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileRequest
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                FileId = reader.GetInt32(reader.GetOrdinal("FileId")),
                RequestedByHost = reader.GetString(reader.GetOrdinal("RequestedByHost")),
                RequestedAt = reader.GetDateTime(reader.GetOrdinal("RequestedAt")),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1
            };
        }
        return null;
    }

    public async Task<bool> DeactivateFileRequestAsync(int fileId, string requestedByHost)
    {
        var query = @"
            UPDATE FileRequests 
            SET IsActive = 0 
            WHERE FileId = @fileId AND RequestedByHost = @requestedByHost AND IsActive = 1";

        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@fileId", fileId);
        command.Parameters.AddWithValue("@requestedByHost", requestedByHost);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}