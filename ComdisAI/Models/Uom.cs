using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Uom : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
