using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Security;

public sealed record IssuedToken(string AccessToken, string Jti, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>Emite JWT HS256 con los claims del contrato: sub, rol, sid, jti, iat, exp.</summary>
public sealed class TokenIssuer(IOptions<AuthOptions> options, TimeProvider time)
{
    public const string RolClaim = "rol";
    public const string SessionClaim = "sid";

    private static readonly JsonWebTokenHandler Handler = new();

    public IssuedToken Issue(Guid userId, string rol, Guid sessionId)
    {
        var opts = options.Value;
        // Segundos enteros: iat/exp del JWT y ExpiresAt en la base quedan idénticos.
        var now = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var expires = now + opts.SessionDuration;
        var jti = Guid.NewGuid().ToString("N");

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [RolClaim] = rol,
                [SessionClaim] = sessionId.ToString(),
                [JwtRegisteredClaimNames.Jti] = jti,
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(opts.SecretBytes), SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, jti, now, expires);
    }

    public static string HashJti(string jti) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jti)));
}
