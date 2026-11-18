using System.Text;
using AuthService.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Tests.Infrastructure;

/// <summary>Tokens armados a mano para probar que el servidor los rechaza.</summary>
public static class ForgedTokens
{
    /// <summary>Claims de un token real (sub, sid apuntan a una sesión activa).</summary>
    public static Dictionary<string, object> ClaimsOf(string realToken, bool keepSid = true)
    {
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(realToken);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = jwt.Subject,
            ["rol"] = RoleNames.Funcionario,
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (keepSid)
            claims["sid"] = jwt.GetClaim("sid").Value;
        return claims;
    }

    public static string Signed(Dictionary<string, object> claims, DateTimeOffset now, string secret = ApiFactory.TestSecret) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.AddMinutes(15),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256),
        });

    /// <summary>Token con alg "none" y sin firma.</summary>
    public static string Unsigned(Dictionary<string, object> claims, DateTimeOffset now)
    {
        claims = new Dictionary<string, object>(claims)
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds(),
        };
        return new JsonWebTokenHandler().CreateToken(System.Text.Json.JsonSerializer.Serialize(claims));
    }
}
