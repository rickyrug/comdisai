using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class User : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    [Required]
    [StringLength(255)]
    public required string Surname { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; set; }

    [Required]
    [StringLength(255)]
    public required string PasswordHash { get; set; }

    public DateTime? LastLoginDateUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
