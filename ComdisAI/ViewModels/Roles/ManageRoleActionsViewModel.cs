using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.Roles;

public sealed class ManageRoleActionsViewModel
{
    public int RoleId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public int ActionId { get; set; }
    public IReadOnlyList<RoleActionListItemViewModel> AssignedActions { get; init; } = [];
    public IReadOnlyList<SelectListItem> AvailableActions { get; init; } = [];
}
