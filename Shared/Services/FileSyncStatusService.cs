using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Data;
using Shared.Models;

namespace Shared.Services;

public interface IFileSyncStatusService
{
    Task<string> GetFileSyncStatusAsync(FileRecord fileRecord);
    Task<List<string>> GetBatchFileSyncStatusAsync(IEnumerable<FileRecord> fileRecords);
    Task<bool> CreateSyncRequestAsync(int fileId);
    Task<bool> IsFilePhysicallyPresentAsync(string relativePath, string fileName);
    string GetInputFolderPath();
    string GetCombinedPath(string relativePath, string fileName);
    string GetFileType(string fileName);
    bool IsMediaFile(string fileName);
}

public class FileSyncStatusService : IFileSyncStatusService
{
    private readonly ILogger<FileSyncStatusService> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileIndexerDatabase _database;
    private readonly string _inputFolderPath;

    public FileSyncStatusService(
        ILogger<FileSyncStatusService> logger,
        IConfiguration configuration,
        FileIndexerDatabase database)
    {
        _logger = logger;
        _configuration = configuration;
        _database = database;
        
        // Get input folder path from configuration
        _inputFolderPath = _configuration["FileIndexerConfiguration:InputFolderPath"] 
                          ?? _configuration["InputFolder"] 
                          ?? throw new InvalidOperationException("InputFolderPath not configured");
    }

    public string GetInputFolderPath()
    {
        return _inputFolderPath;
    }

    public string GetCombinedPath(string relativePath, string fileName)
    {
        return Path.Combine(_inputFolderPath, relativePath, fileName);
    }

    public string GetFileType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        // Image types
        if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".tiff", ".ico" }.Contains(extension))
            return "image";
        
        // Video types
        if (new[] { ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv", ".m4v", ".3gp", ".ogv" }.Contains(extension))
            return "video";
        
        // Audio types
        if (new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" }.Contains(extension))
            return "audio";
        
        // Document types
        if (new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf" }.Contains(extension))
            return "document";
        
        // Archive types
        if (new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" }.Contains(extension))
            return "archive";
        
        // Code types
        if (new[] { ".cs", ".js", ".html", ".css", ".json", ".xml", ".sql", ".py", ".java", ".cpp", ".c", ".h" }.Contains(extension))
            return "code";
        
        return "file";
    }

    public bool IsMediaFile(string fileName)
    {
        var fileType = GetFileType(fileName);
        return fileType == "image" || fileType == "video";
    }

    public Task<bool> IsFilePhysicallyPresentAsync(string relativePath, string fileName)
    {
        try
        {
            var fullPath = Path.Combine(_inputFolderPath, relativePath, fileName);
            return Task.FromResult(File.Exists(fullPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file exists: {RelativePath}", relativePath);
            return Task.FromResult(false);
        }
    }

    public Task<List<string>> GetBatchFileSyncStatusAsync(IEnumerable<FileRecord> fileRecords)
    {
        try
        {
            var results = new List<string>();
            var tasks = new List<Task<string>>();
            
            // Group files by directory to reduce file system calls
            var filesByDirectory = fileRecords.GroupBy(f => f.RelativePath).ToList();
            
            foreach (var directoryGroup in filesByDirectory)
            {
                var directoryPath = Path.Combine(_inputFolderPath, directoryGroup.Key);
                
                // Check if directory exists once per directory
                var directoryExists = Directory.Exists(directoryPath);
                
                foreach (var fileRecord in directoryGroup)
                {
                    if (!fileRecord.IsActive)
                    {
                        results.Add("Deleted");
                        continue;
                    }
                    
                    if (!directoryExists)
                    {
                        results.Add("Not Synced");
                        continue;
                    }
                    
                    // Check file existence within the known existing directory
                    var filePath = Path.Combine(directoryPath, fileRecord.FileName);
                    var fileExists = File.Exists(filePath);
                    results.Add(fileExists ? "Synced" : "Not Synced");
                }
            }
            
            return Task.FromResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch sync status");
            // Return default status for all files
            return Task.FromResult(fileRecords.Select(_ => "Unknown").ToList());
        }
    }

    public async Task<string> GetFileSyncStatusAsync(FileRecord fileRecord)
    {
        try
        {
            // If file is not active, show "Deleted"
            if (!fileRecord.IsActive)
            {
                return "Deleted";
            }

            // Check if file physically exists
            var physicallyPresent = await IsFilePhysicallyPresentAsync(fileRecord.RelativePath, fileRecord.FileName);
            
            if (physicallyPresent)
            {
                return "Synced";
            }
            else
            {
                return "Not Synced";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sync status for file: {RelativePath}", fileRecord.RelativePath);
            return "Unknown";
        }
    }

    public async Task<bool> CreateSyncRequestAsync(int fileId)
    {
        try
        {
            var hostName = Environment.MachineName;
            var result = await _database.CreateFileRequestAsync(fileId, hostName);
            
            if (result)
            {
                _logger.LogInformation("Created sync request for FileId: {FileId} by Host: {Host}", fileId, hostName);
            }
            else
            {
                _logger.LogInformation("Sync request already exists for FileId: {FileId} by Host: {Host}", fileId, hostName);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating sync request for FileId: {FileId}", fileId);
            return false;
        }
    }
}