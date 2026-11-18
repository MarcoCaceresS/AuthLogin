using System.Security.Claims;
using AuthService.Common;
using AuthService.Security;

namespace AuthService.Features.Cuadrilla;

public static class CuadrillaEndpoints
{
    public static IEndpointRouteBuilder MapCuadrilla(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cuadrilla");

        group.MapPost("/login", async (LoginRequest body, HttpContext ctx, SessionService sessions, CancellationToken ct) =>
            ToHttp(await sessions.LoginAsync(
                body.Email,
                body.Password,
                ctx.Connection.RemoteIpAddress?.ToString(),
                ctx.Request.Headers.UserAgent.ToString(),
                ct)));

        group.MapPost("/sesion/renovar", async (ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
                TryGetIds(user, out var userId, out var sessionId)
                    ? ToHttp(await sessions.RenewAsync(userId, sessionId, ct))
                    : ApiErrors.Result(ErrorCodes.NoAutenticado))
            .RequireAuthorization(JwtSetup.FuncionarioPolicy);

        // Best-effort: siempre 204, también con token vencido, inválido o ausente.
        group.MapPost("/logout", async (HttpContext ctx, TokenReader reader, SessionService sessions, CancellationToken ct) =>
        {
            if (await reader.ReadSessionIdAsync(ctx.Request.Headers.Authorization.ToString()) is { } sessionId)
                await sessions.LogoutAsync(sessionId, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        return app;
    }

    /// <summary>Lee sub y sid del token. Tokens sin sid (por ejemplo, de otro emisor) no valen.</summary>
    public static bool TryGetIds(ClaimsPrincipal user, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(user.FindFirstValue("sub"), out userId)
               && Guid.TryParse(user.FindFirstValue(TokenIssuer.SessionClaim), out sessionId);
    }

    private static IResult ToHttp(SessionResult result) =>
        result.Token is { } token ? Results.Ok(token) : ApiErrors.Result(result.ErrorCode!);
}
