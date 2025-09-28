using FileIndexerService.Data;
using FileIndexerService.Models;
using System.Runtime.CompilerServices;

namespace FileIndexerService.Services;

public class FileIndexerService : BackgroundService
{
    private readonly ILogger<FileIndexerService> _logger;
    private readonly IConfiguration _configuration;
    private FileIndexerDatabase? _database;
    
    // Configuration properties
    private string _inputFolderPath = string.Empty;
    private string _targetFolderPath = string.Empty;
    private long _maxBatchSizeBytes = 1024 * 1024 * 1024; // 1GB default
    private int _batchDelayMinutes = 2;
    private string _databasePath = string.Empty;

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
            InitializeDatabase();
            _logger.LogInformation("FileIndexerService started successfully");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during processing cycle");
                }

                // Wait for the configured delay before next cycle
                _logger.LogInformation("Waiting {DelayMinutes} minutes before next cycle...", _batchDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_batchDelayMinutes), stoppingToken);
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
        }
    }

    private void LoadConfiguration()
    {
        var config = _configuration.GetSection("FileIndexerConfiguration");
        _inputFolderPath = config["InputFolderPath"] ?? throw new InvalidOperationException("InputFolderPath is required");
        _targetFolderPath = config["TargetFolderPath"] ?? throw new InvalidOperationException("TargetFolderPath is required");
        _maxBatchSizeBytes = config.GetValue<long>("MaxBatchSizeMB", 1024) * 1024 * 1024; // Convert MB to bytes
        _batchDelayMinutes = config.GetValue<int>("BatchDelayMinutes", 2);
        _databasePath = config["DatabasePath"] ?? Path.Combine(AppContext.BaseDirectory, "fileindexer.db");

        _logger.LogInformation("Configuration loaded - Input: {Input}, Target: {Target}, MaxBatch: {MaxBatch}MB, Delay: {Delay}min", 
            _inputFolderPath, _targetFolderPath, _maxBatchSizeBytes / (1024 * 1024), _batchDelayMinutes);
    }

    private void InitializeDatabase()
    {
        var connectionString = $"Data Source={_databasePath}";
        _database = new FileIndexerDatabase(connectionString);
        _logger.LogInformation("Database initialized at {DatabasePath}", _databasePath);
    }

    private async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting processing cycle...");

        // Step 1: Index new files from input folder
        await IndexNewFilesAsync(cancellationToken);

        // Step 2: Check if target folder is empty
        if (!IsTargetFolderEmpty())
        {
            _logger.LogInformation("Target folder is not empty, skipping batch processing");
            return;
        }

        // Step 3: Get next batch of unprocessed files
        var batch = await GetNextBatchAsync();
        if (batch.Count == 0)
        {
            _logger.LogInformation("No unprocessed files found");
            return;
        }

        // Step 4: Copy files to target folder
        await CopyFilesToTargetAsync(batch, cancellationToken);

        // Step 5: Log statistics
        await LogStatisticsAsync();
    }

    private async Task IndexNewFilesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_inputFolderPath))
        {
            _logger.LogWarning("Input folder does not exist: {InputPath}", _inputFolderPath);
            return;
        }

        _logger.LogInformation("Indexing files in {InputPath}...", _inputFolderPath);
        var newFilesCount = 0;

        await foreach (var filePath in GetAllFilesAsync(_inputFolderPath, cancellationToken))
        {
            try
            {
                var relativePath = GetRelativePath(filePath, _inputFolderPath);
                var fileName = Path.GetFileName(filePath);
                var relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;

                // Check if file already exists in database
                if (await _database!.FileExistsAsync(relativeDir, fileName))
                {
                    // Check if file has been modified
                    var existingRecord = await _database.GetFileRecordAsync(relativeDir, fileName);
                    var fileInfo = new FileInfo(filePath);
                    
                    if (existingRecord?.ModificationDate != fileInfo.LastWriteTime)
                    {
                        // File has been modified, add new record (keeping old one for history)
                        await AddFileRecordAsync(filePath, relativeDir, fileName);
                        newFilesCount++;
                    }
                }
                else
                {
                    // New file, add to database
                    await AddFileRecordAsync(filePath, relativeDir, fileName);
                    newFilesCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
            }
        }

        _logger.LogInformation("Indexed {NewFilesCount} new/modified files", newFilesCount);
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

    private async Task AddFileRecordAsync(string filePath, string relativeDir, string fileName)
    {
        var fileInfo = new FileInfo(filePath);
        var fileRecord = new FileRecord
        {
            RelativePath = relativeDir,
            FileName = fileName,
            FileSizeBytes = fileInfo.Length,
            CreationDate = fileInfo.CreationTime,
            ModificationDate = fileInfo.LastWriteTime,
            IndexedDate = DateTime.Now,
            IsProcessed = false
        };

        await _database!.InsertFileRecordAsync(fileRecord);
    }

    private string GetRelativePath(string fullPath, string basePath)
    {
        var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    private bool IsTargetFolderEmpty()
    {
        if (!Directory.Exists(_targetFolderPath))
        {
            Directory.CreateDirectory(_targetFolderPath);
            return true;
        }

        var files = Directory.GetFiles(_targetFolderPath, "*", SearchOption.AllDirectories);
        return files.Length == 0;
    }

    private async Task<List<FileRecord>> GetNextBatchAsync()
    {
        var batch = await _database!.GetUnprocessedFilesAsync(_maxBatchSizeBytes);
        var totalSize = batch.Sum(f => f.FileSizeBytes);
        
        _logger.LogInformation("Next batch: {FileCount} files, {TotalSizeMB:F2} MB", 
            batch.Count, totalSize / (1024.0 * 1024.0));
        
        return batch;
    }

    private async Task CopyFilesToTargetAsync(List<FileRecord> batch, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Copying {FileCount} files to target folder...", batch.Count);
        var copiedCount = 0;
        var errorCount = 0;

        foreach (var fileRecord in batch)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var sourceFile = Path.Combine(_inputFolderPath, fileRecord.GetFullRelativePath());
                var targetFile = Path.Combine(_targetFolderPath, fileRecord.GetFullRelativePath());
                var targetDir = Path.GetDirectoryName(targetFile);

                // Create target directory if it doesn't exist
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Copy the file
                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, targetFile, overwrite: true);
                    await _database!.MarkFileAsProcessedAsync(fileRecord.Id);
                    copiedCount++;
                    
                    if (copiedCount % 100 == 0) // Log progress every 100 files
                    {
                        _logger.LogInformation("Copied {CopiedCount}/{TotalCount} files...", copiedCount, batch.Count);
                    }
                }
                else
                {
                    _logger.LogWarning("Source file not found: {SourceFile}", sourceFile);
                    errorCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying file: {RelativePath}", fileRecord.GetFullRelativePath());
                errorCount++;
            }
        }

        _logger.LogInformation("Batch copy completed: {CopiedCount} copied, {ErrorCount} errors", copiedCount, errorCount);
    }

    private async Task LogStatisticsAsync()
    {
        var stats = await _database!.GetStatisticsAsync();
        _logger.LogInformation("Database statistics - Total: {Total}, Processed: {Processed}, Unprocessed: {Unprocessed}, Total Size: {TotalSizeMB:F2} MB",
            stats.total, stats.processed, stats.unprocessed, stats.totalSizeBytes / (1024.0 * 1024.0));
    }

    public override void Dispose()
    {
        _database?.Dispose();
        base.Dispose();
    }
}