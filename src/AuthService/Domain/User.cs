namespace AuthService.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
