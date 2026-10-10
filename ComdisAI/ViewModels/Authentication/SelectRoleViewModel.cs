using System.ComponentModel.DataAnnotations;

namespace ComdisAI.ViewModels.Authentication;

public class SelectRoleViewModel
{
    [Required]
    public int? SelectedRoleId { get; set; }

    public string? ReturnUrl { get; set; }
    public IReadOnlyList<RoleSelectionOptionViewModel> Roles { get; set; } = [];
}
