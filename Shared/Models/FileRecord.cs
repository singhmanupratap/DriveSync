using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Models;

[Table("FileRecords")]
public class FileRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(2048)]
    public string RelativePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string FileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime CreationDate { get; set; }

    public DateTime ModificationDate { get; set; }

    public DateTime IndexedDate { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(64)]
    public string? FileHash { get; set; }

    // Full path would be: InputDirectory + RelativePath + FileName
    public string GetFullRelativePath() => Path.Combine(RelativePath, FileName);
    
    // Helper properties for display
    public string FileSizeFormatted => FormatFileSize(FileSizeBytes);
    
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }
}