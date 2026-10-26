using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class Supplier : AuditableEntity
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

    [Display(Name = "Bank")]
    [Range(1, int.MaxValue, ErrorMessage = "Select a bank.")]
    public int BankId { get; set; }
    public Bank? Bank { get; set; }

    [Required]
    [StringLength(25)]
    [Display(Name = "Bank account")]
    public required string BankAccount { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
