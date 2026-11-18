namespace AuthService.Domain;

/// <summary>Una sesión por dispositivo. Su Id viaja en el claim "sid" del JWT.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// SHA-256 (hex) del jti del último token emitido. Solo auditoría: no se valida contra él,
    /// porque un token anterior de la misma sesión debe seguir sirviendo hasta su exp
    /// (si la respuesta de renovar se pierde, la app reintenta con el token viejo).
    /// </summary>
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
