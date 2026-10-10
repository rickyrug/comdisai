using System.ComponentModel.DataAnnotations;

namespace ComdisAI.ViewModels.Users;

public class ResetUserPasswordViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
