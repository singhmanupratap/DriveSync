using Microsoft.AspNetCore.Mvc;
using DriveSync.Shared.Data;
using DriveSync.Shared.Models;
using DriveSync.WebUI.Services;

namespace DriveSync.WebUI.Controllers
{
    public class FilesController : Controller
    {
        private readonly FileIndexerDatabase _database;
        private readonly DatabaseCopyService _databaseCopyService;

        public FilesController(FileIndexerDatabase database, DatabaseCopyService databaseCopyService)
        {
            _database = database;
            _databaseCopyService = databaseCopyService;
        }

        public async Task<IActionResult> Index(int page = 1, int pageSize = 50, string searchTerm = "", string sortBy = "IndexedDate", bool sortAscending = false, bool? isActive = null)
        {
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchTerm = searchTerm;
            ViewBag.SortBy = sortBy;
            ViewBag.SortAscending = sortAscending;
            ViewBag.IsActive = isActive;

            var (files, totalCount) = await _database.GetFileRecordsPagedAsync(page, pageSize, searchTerm, sortBy, sortAscending, isActive);
            
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return View(files);
        }

        [HttpGet]
        public async Task<IActionResult> GetFilesData(int page = 1, int pageSize = 50, string searchTerm = "", string sortBy = "IndexedDate", bool sortAscending = false, bool? isActive = null)
        {
            var (files, totalCount) = await _database.GetFileRecordsPagedAsync(page, pageSize, searchTerm, sortBy, sortAscending, isActive);
            
            return Json(new
            {
                files = files.Select(f => new
                {
                    id = f.Id,
                    fileName = f.FileName,
                    relativePath = f.RelativePath,
                    fullPath = f.GetFullRelativePath(),
                    fileSizeBytes = f.FileSizeBytes,
                    fileSizeFormatted = f.FileSizeFormatted,
                    creationDate = f.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    modificationDate = f.ModificationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    indexedDate = f.IndexedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    isActive = f.IsActive
                }),
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
    }
}