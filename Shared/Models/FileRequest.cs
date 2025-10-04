using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Models;

[Table("FileRequests")]
public class FileRequest
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int FileId { get; set; }

    [Required]
    [MaxLength(256)]
    public string RequestedByHost { get; set; } = string.Empty;

    [Required]
    public DateTime RequestedAt { get; set; }

    [Required]
    public bool IsActive { get; set; } = true;

    // Navigation property
    [ForeignKey("FileId")]
    public virtual FileRecord? FileRecord { get; set; }
}