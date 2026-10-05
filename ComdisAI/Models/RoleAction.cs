namespace ComdisAI.Models;

public class RoleAction
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int ActionId { get; set; }
    public Actions Action { get; set; } = null!;
}
