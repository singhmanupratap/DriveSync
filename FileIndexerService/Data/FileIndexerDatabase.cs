using Microsoft.Data.Sqlite;
using FileIndexerService.Models;
using System.Globalization;

namespace FileIndexerService.Data;

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
                IsProcessed INTEGER NOT NULL DEFAULT 0,
                ProcessedDate TEXT NULL,
                FileHash TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_relativepath ON FileRecords(RelativePath);
            CREATE INDEX IF NOT EXISTS idx_isprocessed ON FileRecords(IsProcessed);
            CREATE INDEX IF NOT EXISTS idx_filename ON FileRecords(FileName);
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
                   IndexedDate, IsProcessed, ProcessedDate, FileHash 
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
                IsProcessed = reader.GetInt32(reader.GetOrdinal("IsProcessed")) == 1,
                ProcessedDate = reader.IsDBNull(reader.GetOrdinal("ProcessedDate")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("ProcessedDate"))),
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            };
        }
        return null;
    }

    public async Task<bool> InsertFileRecordAsync(FileRecord fileRecord)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO FileRecords (RelativePath, FileName, FileSizeBytes, CreationDate, 
                                   ModificationDate, IndexedDate, IsProcessed, FileHash)
            VALUES (@relativePath, @fileName, @fileSizeBytes, @creationDate, 
                    @modificationDate, @indexedDate, @isProcessed, @fileHash)";
        
        command.Parameters.AddWithValue("@relativePath", fileRecord.RelativePath);
        command.Parameters.AddWithValue("@fileName", fileRecord.FileName);
        command.Parameters.AddWithValue("@fileSizeBytes", fileRecord.FileSizeBytes);
        command.Parameters.AddWithValue("@creationDate", fileRecord.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@modificationDate", fileRecord.ModificationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@indexedDate", fileRecord.IndexedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@isProcessed", fileRecord.IsProcessed ? 1 : 0);
        command.Parameters.AddWithValue("@fileHash", fileRecord.FileHash ?? (object)DBNull.Value);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<List<FileRecord>> GetUnprocessedFilesAsync(long maxTotalSizeBytes)
    {
        var files = new List<FileRecord>();
        long currentTotalSize = 0;

        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, RelativePath, FileName, FileSizeBytes, CreationDate, ModificationDate, 
                   IndexedDate, IsProcessed, ProcessedDate, FileHash 
            FROM FileRecords 
            WHERE IsProcessed = 0 
            ORDER BY IndexedDate ASC";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes"));
            
            // Check if adding this file would exceed the size limit
            if (currentTotalSize + fileSizeBytes > maxTotalSizeBytes && files.Count > 0)
            {
                break; // Stop adding files if we would exceed the limit
            }

            var fileRecord = new FileRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSizeBytes = fileSizeBytes,
                CreationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationDate"))),
                ModificationDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModificationDate"))),
                IndexedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("IndexedDate"))),
                IsProcessed = reader.GetInt32(reader.GetOrdinal("IsProcessed")) == 1,
                ProcessedDate = reader.IsDBNull(reader.GetOrdinal("ProcessedDate")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("ProcessedDate"))),
                FileHash = reader.IsDBNull(reader.GetOrdinal("FileHash")) ? null : reader.GetString(reader.GetOrdinal("FileHash"))
            };

            files.Add(fileRecord);
            currentTotalSize += fileSizeBytes;
        }

        return files;
    }

    public async Task<bool> MarkFileAsProcessedAsync(int fileId)
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileRecords 
            SET IsProcessed = 1, ProcessedDate = @processedDate 
            WHERE Id = @id";
        command.Parameters.AddWithValue("@processedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@id", fileId);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<(int total, int processed, int unprocessed, long totalSizeBytes)> GetStatisticsAsync()
    {
        var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN IsProcessed = 1 THEN 1 ELSE 0 END) as Processed,
                SUM(CASE WHEN IsProcessed = 0 THEN 1 ELSE 0 END) as Unprocessed,
                SUM(FileSizeBytes) as TotalSizeBytes
            FROM FileRecords";

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetInt32(reader.GetOrdinal("Total")),
                reader.GetInt32(reader.GetOrdinal("Processed")),
                reader.GetInt32(reader.GetOrdinal("Unprocessed")),
                reader.GetInt64(reader.GetOrdinal("TotalSizeBytes"))
            );
        }

        return (0, 0, 0, 0);
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