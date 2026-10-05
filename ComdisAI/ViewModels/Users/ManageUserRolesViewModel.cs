namespace ComdisAI.ViewModels.Users;

public sealed class ManageUserRolesViewModel
{
    public int? UserId { get; init; }
    public List<int> SelectedRoleIds { get; set; } = [];
    public IReadOnlyList<UserListItemViewModel> Users { get; init; } = [];
    public IReadOnlyList<UserRoleOptionViewModel> Roles { get; init; } = [];
}
