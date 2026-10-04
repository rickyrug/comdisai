using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Customer : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    [Required]
    [StringLength(255)]
    public required string Address { get; set; }

    [Required]
    [StringLength(25)]
    public required string RFC { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
