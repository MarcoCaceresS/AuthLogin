namespace AuthService.Domain;

/// <summary>Una sesión por dispositivo. Su Id viaja en el claim "sid" del JWT.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 (hex) del jti del último token emitido para esta sesión.</summary>
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
