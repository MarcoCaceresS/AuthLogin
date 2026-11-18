namespace AuthService.Domain;

/// <summary>Registro de un login exitoso.</summary>
public sealed class LoginRecord
{
    public const int UserAgentMaxLength = 512;

    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
