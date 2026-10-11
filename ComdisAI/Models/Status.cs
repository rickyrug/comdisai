using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Status : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    [Required]
    [StringLength(20)]
    public required string Entity { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
