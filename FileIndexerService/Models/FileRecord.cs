using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileIndexerService.Models;

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

    public bool IsProcessed { get; set; }

    public DateTime? ProcessedDate { get; set; }

    [MaxLength(64)]
    public string? FileHash { get; set; }

    // Full path would be: TargetDirectory + RelativePath + FileName
    public string GetFullRelativePath() => Path.Combine(RelativePath, FileName);
}