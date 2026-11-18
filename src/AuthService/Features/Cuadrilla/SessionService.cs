using AuthService.Common;
using AuthService.Data;
using AuthService.Domain;
using AuthService.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthService.Features.Cuadrilla;

public sealed class SessionService(
    AuthDbContext db,
    IPasswordHasher<User> hasher,
    TokenIssuer issuer,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    // Hash fijo para comparar cuando el correo no existe: el tiempo de respuesta no revela si existe.
    private static readonly User DummyUser = new() { Email = "", FirstName = "", LastName = "" };
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<User>().HashPassword(DummyUser, "clave-ficticia-para-tiempo-constante"));

    public async Task<SessionResult> LoginAsync(
        string? email, string? password, string? ip, string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return SessionResult.Fail(ErrorCodes.DatosInvalidos);

        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Email == normalized, ct);

        if (user is null)
        {
            hasher.VerifyHashedPassword(DummyUser, DummyHash.Value, password);
            return SessionResult.Fail(ErrorCodes.CredencialesInvalidas);
        }

        var check = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (check == PasswordVerificationResult.Failed)
            return SessionResult.Fail(ErrorCodes.CredencialesInvalidas);
        if (check == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);

        if (!user.IsActive || !user.IsEmailVerified)
            return SessionResult.Fail(ErrorCodes.CredencialesInvalidas);

        var session = new UserSession { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "" };
        var token = issuer.Issue(user.Id, user.Role.Name, session.Id);
        session.TokenHash = TokenIssuer.HashJti(token.Jti);
        session.CreatedAt = token.IssuedAt;
        session.ExpiresAt = token.ExpiresAt;
        db.UserSessions.Add(session);
        db.LoginRecords.Add(new LoginRecord
        {
            UserId = user.Id,
            SessionId = session.Id,
            OccurredAt = token.IssuedAt,
            IpAddress = ip,
            UserAgent = Truncate(userAgent, LoginRecord.UserAgentMaxLength),
        });
        await db.SaveChangesAsync(ct);

        return new SessionResult(ToResponse(user, token), null);
    }

    /// <summary>Extiende la sesión otros N días y emite un token nuevo con el mismo sid.</summary>
    public async Task<SessionResult> RenewAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.UserSessions
            .Include(s => s.User).ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
            return SessionResult.Fail(ErrorCodes.NoAutenticado);

        var user = session.User;
        // Antes que la revocación: desactivar revoca sesiones y la app debe ver «cuenta desactivada».
        if (!user.IsActive)
            return SessionResult.Fail(ErrorCodes.CuentaInactiva);
        if (session.RevokedAt is not null || session.ExpiresAt <= time.GetUtcNow())
            return SessionResult.Fail(ErrorCodes.NoAutenticado);

        var token = issuer.Issue(user.Id, user.Role.Name, session.Id);
        session.TokenHash = TokenIssuer.HashJti(token.Jti);
        session.ExpiresAt = token.ExpiresAt;
        await db.SaveChangesAsync(ct);

        return new SessionResult(ToResponse(user, token), null);
    }

    /// <summary>Rol actual en la base si la sesión sigue abierta y la cuenta activa; si no, null.</summary>
    public Task<string?> ActiveSessionRoleAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && s.User.IsActive)
            .Select(s => s.User.Role.Name)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Revoca la sesión. Idempotente: revocar dos veces no cambia la fecha.</summary>
    public Task LogoutAsync(Guid sessionId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.UserSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    private TokenResponse ToResponse(User user, IssuedToken token) => new(
        token.AccessToken,
        "bearer",
        options.Value.SessionSeconds,
        new FuncionarioOut(user.Id, user.Email, user.FullName, user.Role.Name, user.IsActive));

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
