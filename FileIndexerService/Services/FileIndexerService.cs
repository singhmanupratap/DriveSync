using DriveSync.Shared.Data;
using DriveSync.Shared.Models;
using System.Runtime.CompilerServices;

namespace FileIndexerService.Services;

public class FileIndexerService : BackgroundService
{
    private readonly ILogger<FileIndexerService> _logger;
    private readonly IConfiguration _configuration;
    private FileIndexerDatabase? _database;
    
    // Configuration properties
    private string _inputFolderPath = string.Empty;
    private int _scanIntervalMinutes = 5;
    private string _databasePath = string.Empty;
    private string _localDatabasePath = string.Empty;

    public FileIndexerService(ILogger<FileIndexerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            LoadConfiguration();
            
            // Initial database copy and setup
            await CopyDatabaseToLocalAsync();
            InitializeDatabase();
            
            _logger.LogInformation("FileIndexerService started successfully - Monitoring folder: {InputFolder}", _inputFolderPath);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await IndexFilesAsync(stoppingToken);
                    await LogStatisticsAsync();
                    await CopyDatabaseToRemoteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during file indexing cycle");
                }

                // Wait for the configured interval before next scan
                _logger.LogDebug("Waiting {ScanInterval} minutes before next scan...", _scanIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_scanIntervalMinutes), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error in FileIndexerService");
            throw;
        }
        finally
        {
            _database?.Dispose();
            
            // Final copy of database to remote location
            try
            {
                await CopyDatabaseToRemoteAsync();
                _logger.LogInformation("Final database copy completed");
                
                // Clean up local database file
                if (File.Exists(_localDatabasePath))
                {
                    File.Delete(_localDatabasePath);
                    _logger.LogInformation("Local database file cleaned up");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final database copy and cleanup");
            }
        }
    }

    private void LoadConfiguration()
    {
        var config = _configuration.GetSection("FileIndexerConfiguration");
        _inputFolderPath = config["InputFolderPath"] ?? throw new InvalidOperationException("InputFolderPath is required");
        _scanIntervalMinutes = config.GetValue<int>("ScanIntervalMinutes", 5);
        _databasePath = config["DatabasePath"] ?? throw new InvalidOperationException("DatabasePath is required");
        _localDatabasePath = config["LocalDatabasePath"] ?? Path.Combine(AppContext.BaseDirectory, "fileindexer_local.db");

        _logger.LogInformation("Configuration loaded - Input: {Input}, ScanInterval: {Interval}min, Database: {Database}, LocalDatabase: {LocalDatabase}", 
            _inputFolderPath, _scanIntervalMinutes, _databasePath, _localDatabasePath);
    }

    private void InitializeDatabase()
    {
        var connectionString = $"Data Source={_localDatabasePath}";
        _database = new FileIndexerDatabase(connectionString);
        _logger.LogInformation("Database initialized at local path: {LocalDatabasePath}", _localDatabasePath);
    }

    private async Task IndexFilesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_inputFolderPath))
        {
            _logger.LogWarning("Input folder does not exist: {InputPath}", _inputFolderPath);
            return;
        }

        _logger.LogInformation("Starting file indexing scan of {InputPath}...", _inputFolderPath);
        var newFilesCount = 0;
        var updatedFilesCount = 0;

        await foreach (var filePath in GetAllFilesAsync(_inputFolderPath, cancellationToken))
        {
            try
            {
                var relativePath = GetRelativePath(filePath, _inputFolderPath);
                var fileName = Path.GetFileName(filePath);
                var relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                var fileInfo = new FileInfo(filePath);

                // Check if file already exists in database
                if (await _database!.FileExistsAsync(relativeDir, fileName))
                {
                    // Check if file has been modified
                    var existingRecord = await _database.GetFileRecordAsync(relativeDir, fileName);
                    
                    if (existingRecord != null && existingRecord.ModificationDate != fileInfo.LastWriteTime)
                    {
                        // File has been modified, update the record
                        await UpdateFileRecordAsync(existingRecord.Id, fileInfo);
                        updatedFilesCount++;
                        _logger.LogDebug("Updated modified file: {RelativePath}", relativePath);
                    }
                }
                else
                {
                    // New file, add to database
                    await AddFileRecordAsync(filePath, relativeDir, fileName, fileInfo);
                    newFilesCount++;
                    _logger.LogDebug("Indexed new file: {RelativePath}", relativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
            }
        }

        _logger.LogInformation("File indexing completed - New: {NewFiles}, Updated: {UpdatedFiles}", 
            newFilesCount, updatedFilesCount);
    }

    private async IAsyncEnumerable<string> GetAllFilesAsync(string folderPath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var directories = new Queue<string>();
        directories.Enqueue(folderPath);

        while (directories.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = directories.Dequeue();
            
            // Get files in current directory
            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to directory: {Directory}", currentDir);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing directory: {Directory}", currentDir);
                continue;
            }

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                yield return file;
            }

            // Add subdirectories to queue
            try
            {
                var subdirs = Directory.GetDirectories(currentDir);
                foreach (var subdir in subdirs)
                {
                    directories.Enqueue(subdir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to subdirectories in: {Directory}", currentDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subdirectories in: {Directory}", currentDir);
            }
        }
    }

    private async Task AddFileRecordAsync(string filePath, string relativeDir, string fileName, FileInfo fileInfo)
    {
        var fileRecord = new FileRecord
        {
            RelativePath = relativeDir,
            FileName = fileName,
            FileSizeBytes = fileInfo.Length,
            CreationDate = fileInfo.CreationTime,
            ModificationDate = fileInfo.LastWriteTime,
            IndexedDate = DateTime.Now,
            FileHash = null // Could add file hash calculation if needed
        };

        await _database!.InsertFileRecordAsync(fileRecord);
    }

    private async Task UpdateFileRecordAsync(int recordId, FileInfo fileInfo)
    {
        // For simplicity, we'll add a new record instead of updating
        // This maintains a history of file changes
        var relativePath = GetRelativePath(fileInfo.FullName, _inputFolderPath);
        var fileName = Path.GetFileName(fileInfo.FullName);
        var relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;

        await AddFileRecordAsync(fileInfo.FullName, relativeDir, fileName, fileInfo);
    }

    private string GetRelativePath(string fullPath, string basePath)
    {
        var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    private async Task CopyDatabaseToLocalAsync()
    {
        try
        {
            // Copy database from remote path to local path before processing
            if (File.Exists(_databasePath))
            {
                _logger.LogInformation("Copying database from {RemotePath} to {LocalPath}", _databasePath, _localDatabasePath);
                
                // Ensure the local directory exists
                var localDir = Path.GetDirectoryName(_localDatabasePath);
                if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }
                
                // Copy the file
                File.Copy(_databasePath, _localDatabasePath, overwrite: true);
                _logger.LogInformation("Database copied successfully to local path");
            }
            else
            {
                _logger.LogInformation("Remote database does not exist, will create new local database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying database to local path");
            throw;
        }
    }

    private async Task CopyDatabaseToRemoteAsync()
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
                _logger.LogWarning("Local database does not exist, cannot copy to remote path");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying database to remote path");
            // Don't throw here as we don't want to crash the service for copy failures
        }
    }

    private async Task LogStatisticsAsync()
    {
        var stats = await _database!.GetStatisticsAsync();
        _logger.LogInformation("Database statistics - Total: {Total}, Active: {Active}, Inactive: {Inactive}, Total size: {TotalSizeMB:F2} MB",
            stats.total, stats.active, stats.inactive, stats.totalSizeBytes / (1024.0 * 1024.0));
    }

    public override void Dispose()
    {
        _database?.Dispose();
        base.Dispose();
    }
}