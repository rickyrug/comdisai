using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Actions : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public required string Code { get; set; }

    [Required]
    [StringLength(255)]
    public required string Description { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
