namespace ComdisAI.ViewModels.Roles;

public sealed class RoleActionListItemViewModel
{
    public int RoleActionId { get; init; }
    public int ActionId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
}
