namespace ComdisAI.ViewModels.Users;

public class UserListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastLoginDateUtc { get; set; }
}
