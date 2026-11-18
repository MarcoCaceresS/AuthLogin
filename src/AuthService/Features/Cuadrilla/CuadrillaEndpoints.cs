using AuthService.Common;

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

        return app;
    }

    private static IResult ToHttp(SessionResult result) =>
        result.Token is { } token ? Results.Ok(token) : ApiErrors.Result(result.ErrorCode!);
}
