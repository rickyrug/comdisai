namespace ComdisAI.ViewModels.Roles;

public sealed class ManageRoleActionsViewModel
{
    public int RoleId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public List<int> SelectedActionIds { get; set; } = [];
    public List<int> SelectedRoleActionIds { get; set; } = [];
    public IReadOnlyList<RoleActionListItemViewModel> AssignedActions { get; init; } = [];
    public IReadOnlyList<RoleActionOptionViewModel> AvailableActions { get; init; } = [];
}
