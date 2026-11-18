using System.Threading.RateLimiting;
using AuthService.Common;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace AuthService.Security;

public static class RateLimits
{
    public const string LoginPolicy = "login";
    public const string RenewPolicy = "renovar";

    /// <summary>
    /// Login: por IP (contrato: 10/min). Renovar: por funcionario, aparte del login,
    /// porque varias cuadrillas comparten la misma IP de red móvil.
    /// </summary>
    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var loginPerMinute = config.GetValue("RateLimits:LoginPerMinute", 10);
        var renewPerMinute = config.GetValue("RateLimits:RenewPerMinute", 30);

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(LoginPolicy, ctx => Fixed(ctx.Connection.RemoteIpAddress?.ToString() ?? "sin-ip", loginPerMinute));
            o.AddPolicy(RenewPolicy, ctx =>
                Fixed(ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "sin-ip", renewPerMinute));
            o.OnRejected = (ctx, _) => new ValueTask(ApiErrors.WriteAsync(ctx.HttpContext, ErrorCodes.DemasiadosIntentos));
        });

        // Detrás del proxy de Render la IP real viene en X-Forwarded-For. Solo se activa a pedido:
        // sin proxy delante, cualquiera podría falsificar su IP con ese header.
        if (config.GetValue("ForwardedHeaders:Enabled", false))
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        return services;
    }

    private static RateLimitPartition<string> Fixed(string key, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
