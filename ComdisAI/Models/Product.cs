using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Product : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    [Required]
    [StringLength(5)]
    public required string Code { get; set; }

    public int UomId { get; set; }
    public Uom Uom { get; set; } = null!;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
