using Microsoft.AspNetCore.Mvc;
using Shared.Data;
using Shared.Models;
using Shared.Services;

namespace DriveSync.WebUI.Controllers
{
    public class FilesController : Controller
    {
        private readonly FileIndexerDatabase _database;
        private readonly IDatabaseCopyService _databaseCopyService;
        private readonly IFileSyncStatusService _fileSyncStatusService;
        private readonly ILogger<FilesController> _logger;

        public FilesController(FileIndexerDatabase database, IDatabaseCopyService databaseCopyService, IFileSyncStatusService fileSyncStatusService, ILogger<FilesController> logger)
        {
            _database = database;
            _databaseCopyService = databaseCopyService;
            _fileSyncStatusService = fileSyncStatusService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int page = 1, int pageSize = 50, string searchTerm = "", string sortBy = "IndexedDate", bool sortAscending = false, bool? isActive = null)
        {
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchTerm = searchTerm;
            ViewBag.SortBy = sortBy;
            ViewBag.SortAscending = sortAscending;
            ViewBag.IsActive = isActive;
            ViewBag.InputFolderPath = _fileSyncStatusService.GetInputFolderPath();

            var (files, totalCount) = await _database.GetFileRecordsPagedAsync(page, pageSize, searchTerm, sortBy, sortAscending, isActive);
            
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return View(files);
        }

        [HttpGet]
        public async Task<IActionResult> GetFilesData(int page = 1, int pageSize = 50, string searchTerm = "", string sortBy = "IndexedDate", bool sortAscending = false, bool? isActive = null)
        {
            var (files, totalCount) = await _database.GetFileRecordsPagedAsync(page, pageSize, searchTerm, sortBy, sortAscending, isActive);
            
            // Get batch sync status for better performance
            var syncStatuses = await _fileSyncStatusService.GetBatchFileSyncStatusAsync(files);
            
            // Build response objects efficiently
            var filesWithStatus = files.Select((f, index) => new
            {
                id = f.Id,
                fileName = f.FileName,
                relativePath = f.RelativePath,
                fullPath = f.GetFullRelativePath(),
                combinedPath = _fileSyncStatusService.GetCombinedPath(f.RelativePath, f.FileName),
                fileSizeBytes = f.FileSizeBytes,
                fileSizeFormatted = f.FileSizeFormatted,
                creationDate = f.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                modificationDate = f.ModificationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                indexedDate = f.IndexedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                isActive = f.IsActive,
                syncStatus = syncStatuses[index],
                fileType = _fileSyncStatusService.GetFileType(f.FileName),
                isMediaFile = _fileSyncStatusService.IsMediaFile(f.FileName)
            }).ToList();
            
            return Json(new
            {
                files = filesWithStatus,
                totalCount = totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                currentPage = page
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFile(int id)
        {
            try
            {
                var currentTime = DateTime.Now;
                var success = await _database.MarkFileAsInactiveWithTimestampAsync(id);
                
                if (success)
                {
                    // Sync the change back to the original database
                    _ = Task.Run(async () => await _databaseCopyService.SyncRecordToRemoteAsync(id, false, currentTime));
                }
                
                return Json(new { success = success, message = success ? "File marked as inactive successfully." : "Failed to mark file as inactive." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SyncToRemote()
        {
            try
            {
                await _databaseCopyService.SyncAllChangesToRemoteAsync();
                return Json(new { success = true, message = "Database synced to remote successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error syncing to remote: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EmergencySync()
        {
            try
            {
                // Emergency sync endpoint for critical situations
                await _databaseCopyService.SyncAllChangesToRemoteAsync();
                return Json(new { success = true, message = "Emergency database sync completed successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Emergency sync failed: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteFiles(List<int> fileIds)
        {
            try
            {
                if (fileIds == null || !fileIds.Any())
                {
                    return Json(new { success = false, message = "No files selected for deletion." });
                }

                var currentTime = DateTime.Now;
                var successCount = 0;
                var errorCount = 0;

                foreach (var fileId in fileIds)
                {
                    try
                    {
                        var success = await _database.MarkFileAsInactiveWithTimestampAsync(fileId);
                        if (success)
                        {
                            successCount++;
                            // Sync the change back to the original database
                            _ = Task.Run(async () => await _databaseCopyService.SyncRecordToRemoteAsync(fileId, false, currentTime));
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                    catch
                    {
                        errorCount++;
                    }
                }

                var message = $"Successfully marked {successCount} file(s) as inactive.";
                if (errorCount > 0)
                {
                    message += $" {errorCount} file(s) failed to update.";
                }

                return Json(new { 
                    success = successCount > 0, 
                    message = message,
                    successCount = successCount,
                    errorCount = errorCount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error during bulk delete: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestSync(int fileId)
        {
            try
            {
                var success = await _fileSyncStatusService.CreateSyncRequestAsync(fileId);
                
                if (success)
                {
                    return Json(new { success = true, message = "Sync request created successfully." });
                }
                else
                {
                    return Json(new { success = false, message = "Sync request already exists or could not be created." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error creating sync request: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMediaFile(int fileId)
        {
            try
            {
                var fileRecord = await _database.GetFileRecordByIdAsync(fileId);
                if (fileRecord == null || !fileRecord.IsActive)
                {
                    return NotFound("File not found or inactive");
                }

                var combinedPath = _fileSyncStatusService.GetCombinedPath(fileRecord.RelativePath, fileRecord.FileName);
                
                if (!System.IO.File.Exists(combinedPath))
                {
                    return NotFound("Physical file not found");
                }

                var fileType = _fileSyncStatusService.GetFileType(fileRecord.FileName);
                if (fileType != "image" && fileType != "video")
                {
                    return BadRequest("File is not a media file");
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(combinedPath);
                var contentType = GetContentType(fileRecord.FileName, fileType);
                
                return File(fileBytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving media file with ID: {FileId}", fileId);
                return StatusCode(500, "Error serving media file");
            }
        }

        private string GetContentType(string fileName, string fileType)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            
            if (fileType == "image")
            {
                return extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    ".svg" => "image/svg+xml",
                    ".tiff" => "image/tiff",
                    ".ico" => "image/x-icon",
                    _ => "image/jpeg"
                };
            }
            else if (fileType == "video")
            {
                return extension switch
                {
                    ".mp4" => "video/mp4",
                    ".avi" => "video/x-msvideo",
                    ".mov" => "video/quicktime",
                    ".wmv" => "video/x-ms-wmv",
                    ".flv" => "video/x-flv",
                    ".webm" => "video/webm",
                    ".mkv" => "video/x-matroska",
                    ".m4v" => "video/x-m4v",
                    ".3gp" => "video/3gpp",
                    ".ogv" => "video/ogg",
                    _ => "video/mp4"
                };
            }
            
            return "application/octet-stream";
        }
    }
}