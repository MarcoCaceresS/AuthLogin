using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Security;

/// <summary>Lee el sid de un token propio aunque esté vencido (para logout). La firma sí se exige.</summary>
public sealed class TokenReader(IOptions<AuthOptions> options)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<Guid?> ReadSessionIdAsync(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var result = await Handler.ValidateTokenAsync(authorizationHeader[prefix.Length..].Trim(), new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new SymmetricSecurityKey(options.Value.SecretBytes),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        });

        return result.IsValid
               && result.Claims.TryGetValue(TokenIssuer.SessionClaim, out var sid)
               && Guid.TryParse(sid as string, out var id)
            ? id
            : null;
    }
}
