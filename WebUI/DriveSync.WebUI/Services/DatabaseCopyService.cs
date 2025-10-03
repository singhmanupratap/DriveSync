namespace DriveSync.WebUI.Services
{
    public class DatabaseCopyService
    {
        private readonly ILogger<DatabaseCopyService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _databasePath;
        private readonly string _localDatabasePath;

        public DatabaseCopyService(ILogger<DatabaseCopyService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            var dbConfig = _configuration.GetSection("DatabaseConfig");
            _databasePath = dbConfig["DatabasePath"] ?? throw new InvalidOperationException("DatabasePath not configured");
            _localDatabasePath = dbConfig["LocalDatabasePath"] ?? "fileindexer_local.db";
        }

        public Task CopyDatabaseToLocalAsync()
        {
            try
            {
                // Only copy if local database doesn't exist to preserve local changes
                if (!File.Exists(_localDatabasePath))
                {
                    // Copy database from remote path to local path before processing
                    if (File.Exists(_databasePath))
                    {
                        _logger.LogInformation("Local database not found. Copying database from {RemotePath} to {LocalPath}", _databasePath, _localDatabasePath);
                        
                        // Ensure the local directory exists
                        var localDir = Path.GetDirectoryName(_localDatabasePath);
                        if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                        {
                            Directory.CreateDirectory(localDir);
                        }
                        
                        // Copy the file
                        File.Copy(_databasePath, _localDatabasePath, overwrite: false);
                        _logger.LogInformation("Database copied successfully to local path");
                    }
                    else
                    {
                        _logger.LogWarning("Remote database does not exist at {DatabasePath}, will create new empty local database", _databasePath);
                        
                        // The FileIndexerDatabase.InitializeDatabase() will create the schema when needed
                        _logger.LogInformation("Creating new empty local database at {LocalPath}", _localDatabasePath);
                    }
                }
                else
                {
                    _logger.LogInformation("Local database already exists at {LocalPath}, preserving existing data", _localDatabasePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying database to local path");
                throw;
            }
            
            return Task.CompletedTask;
        }

        public Task CopyDatabaseToRemoteAsync()
        {
            try
            {
                // Copy database from local path back to remote path after processing
                if (File.Exists(_localDatabasePath))
                {
                    _logger.LogInformation("Copying database from {LocalPath} to {RemotePath}", _localDatabasePath, _databasePath);
                    
                    // Ensure the remote directory exists
                    var remoteDir = Path.GetDirectoryName(_databasePath);
                    if (!string.IsNullOrEmpty(remoteDir) && !Directory.Exists(remoteDir))
                    {
                        Directory.CreateDirectory(remoteDir);
                    }
                    
                    // Copy the file
                    File.Copy(_localDatabasePath, _databasePath, overwrite: true);
                    _logger.LogInformation("Database copied successfully to remote path");
                }
                else
                {
                    _logger.LogWarning("Local database does not exist, nothing to copy to remote");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying database to remote path");
                // Don't throw here - this is less critical than copying from remote
            }
            
            return Task.CompletedTask;
        }

        public async Task SyncRecordToRemoteAsync(int recordId, bool isActive, DateTime modificationDate)
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    _logger.LogWarning("Remote database does not exist at {DatabasePath}, cannot sync record", _databasePath);
                    return;
                }

                _logger.LogInformation("Syncing record {RecordId} (IsActive: {IsActive}) to remote database", recordId, isActive);
                
                // Open connection to remote database and update the specific record
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
                await connection.OpenAsync();
                
                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE FileRecords 
                    SET IsActive = @isActive,
                        ModificationDate = @modificationDate
                    WHERE Id = @id";
                command.Parameters.AddWithValue("@id", recordId);
                command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                command.Parameters.AddWithValue("@modificationDate", modificationDate.ToString("yyyy-MM-dd HH:mm:ss"));

                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Successfully synced record {RecordId} to remote database", recordId);
                }
                else
                {
                    _logger.LogWarning("No rows were updated when syncing record {RecordId} to remote database", recordId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing record {RecordId} to remote database", recordId);
                // Don't throw - this shouldn't break the main operation
            }
        }

        public async Task SyncAllChangesToRemoteAsync()
        {
            try
            {
                if (!File.Exists(_localDatabasePath) || !File.Exists(_databasePath))
                {
                    _logger.LogWarning("Local or remote database missing, cannot perform sync");
                    return;
                }

                _logger.LogInformation("Performing full sync from local to remote database");
                
                // Simple approach: copy the entire local database to remote
                // For production, you might want a more sophisticated sync mechanism
                await CopyDatabaseToRemoteAsync();
                
                _logger.LogInformation("Full sync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during full sync to remote database");
                throw; // Re-throw to allow caller to handle appropriately
            }
        }
    }
}