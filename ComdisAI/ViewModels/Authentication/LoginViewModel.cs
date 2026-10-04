using System.ComponentModel.DataAnnotations;

namespace ComdisAI.ViewModels.Authentication;

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
