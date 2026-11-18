using AuthService.Common;
using AuthService.Domain;
using AuthService.Features.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Security;

public static class JwtSetup
{
    public const string FuncionarioPolicy = "Funcionario";
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddFuncionarioAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, TimeProvider>((o, auth, time) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    // Sin iss/aud: los tokens deben ser compatibles con los que valida FastAPI.
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(auth.Value.SecretBytes),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    LifetimeValidator = (notBefore, expires, _, _) => ValidateLifetime(notBefore, expires, time),
                    NameClaimType = "sub",
                    RoleClaimType = TokenIssuer.RolClaim,
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        ctx.HandleResponse();
                        var codigo = ctx.AuthenticateFailure is SecurityTokenExpiredException
                            ? ErrorCodes.SesionExpirada
                            : ErrorCodes.NoAutenticado;
                        return ApiErrors.WriteAsync(ctx.HttpContext, codigo);
                    },
                    OnForbidden = ctx => ApiErrors.WriteAsync(ctx.HttpContext, ErrorCodes.SinPermiso,
                        ctx.Request.Path.StartsWithSegments(AdminEndpoints.Prefix) ? AdminEndpoints.SinPermisoMensaje : null),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(FuncionarioPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => RoleNames.AppFuncionarios.Contains(ctx.User.FindFirst(TokenIssuer.RolClaim)?.Value ?? "")))
            .AddPolicy(AdminPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireClaim(TokenIssuer.RolClaim, RoleNames.Superadmin));

        return services;
    }

    // Usa el TimeProvider inyectado (tests con reloj fijo) y sin tolerancia: vencido es vencido.
    private static bool ValidateLifetime(DateTime? notBefore, DateTime? expires, TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (expires is null || now >= expires)
            throw new SecurityTokenExpiredException("Token vencido.") { Expires = expires ?? DateTime.MinValue };
        if (notBefore is not null && now < notBefore)
            throw new SecurityTokenNotYetValidException("Token aún no válido.") { NotBefore = notBefore.Value };
        return true;
    }
}
