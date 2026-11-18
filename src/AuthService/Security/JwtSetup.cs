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
                    // Rol desconocido (por ejemplo "usuario" de la app de vecinos) = token inválido → 401.
                    OnTokenValidated = ctx =>
                    {
                        var rol = ctx.Principal?.FindFirst(TokenIssuer.RolClaim)?.Value;
                        if (rol is null || !RoleNames.AppFuncionarios.Contains(rol))
                            ctx.Fail("Rol no válido para este servicio.");
                        return Task.CompletedTask;
                    },
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
            .AddPolicy(FuncionarioPolicy, p => p.RequireAuthenticatedUser())
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
