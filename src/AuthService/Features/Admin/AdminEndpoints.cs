using System.Security.Claims;
using AuthService.Common;
using AuthService.Features.Cuadrilla;
using AuthService.Security;

namespace AuthService.Features.Admin;

public static class AdminEndpoints
{
    public const string Prefix = "/api/v1/admin";
    public const string SinPermisoMensaje = "No tienes permiso para realizar esta acción.";

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{Prefix}/usuarios")
            .RequireAuthorization(JwtSetup.AdminPolicy)
            .AddEndpointFilter(RequireActiveSession);

        group.MapPost("/", async (CrearUsuarioRequest body, UserAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.CreateAsync(body, ct);
            return result.Value is { } created
                ? Results.Created((string?)null, created)
                : ApiErrors.Result(result.ErrorCode!, result.Mensaje);
        });

        group.MapGet("/", async (bool? activo, string? rol, UserAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.ListAsync(activo, rol, ct)));

        return app;
    }

    /// <summary>
    /// Las acciones de admin revisan en la base, en cada request, que la sesión siga abierta
    /// y la cuenta activa: un admin desactivado no puede seguir usando su token.
    /// </summary>
    private static async ValueTask<object?> RequireActiveSession(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var sessions = http.RequestServices.GetRequiredService<SessionService>();
        if (!CuadrillaEndpoints.TryGetIds(http.User, out var userId, out var sessionId)
            || !await sessions.IsSessionActiveAsync(userId, sessionId, http.RequestAborted))
            return ApiErrors.Result(ErrorCodes.NoAutenticado);

        return await next(ctx);
    }

    public static Guid CurrentUserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
}
