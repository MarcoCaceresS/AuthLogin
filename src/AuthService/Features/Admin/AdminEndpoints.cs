using AuthService.Common;
using AuthService.Domain;
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
            ToHttp(await admin.CreateAsync(body, ct), created => Results.Created((string?)null, created)));

        group.MapGet("/", async (bool? activo, string? rol, UserAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.ListAsync(activo, rol, ct)));

        group.MapPatch("/{id:guid}/estado", async (Guid id, CambiarEstadoRequest body, UserAdminService admin, CancellationToken ct) =>
            ToHttp(await admin.SetActiveAsync(id, body.Activo, ct), Results.Ok));

        group.MapPost("/{id:guid}/clave", async (Guid id, ResetClaveRequest body, UserAdminService admin, CancellationToken ct) =>
            ToHttp(await admin.ResetPasswordAsync(id, body.Password, ct), _ => Results.NoContent()));

        group.MapGet("/{id:guid}/ingresos", async (Guid id, UserAdminService admin, CancellationToken ct) =>
            ToHttp(await admin.LoginHistoryAsync(id, ct), Results.Ok));

        return app;
    }

    private static IResult ToHttp<T>(AdminResult<T> result, Func<T, IResult> ok) =>
        result.ErrorCode is null
            ? ok(result.Value!)
            : ApiErrors.Result(result.ErrorCode,
                result.Mensaje ?? (result.ErrorCode == ErrorCodes.SinPermiso ? SinPermisoMensaje : null));

    /// <summary>
    /// Las acciones de admin revisan en la base, en cada request, que la sesión siga abierta,
    /// la cuenta activa y el rol siga siendo superadmin: el token solo no basta.
    /// </summary>
    private static async ValueTask<object?> RequireActiveSession(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var sessions = http.RequestServices.GetRequiredService<SessionService>();
        if (!CuadrillaEndpoints.TryGetIds(http.User, out var userId, out var sessionId))
            return ApiErrors.Result(ErrorCodes.NoAutenticado);

        var rol = await sessions.ActiveSessionRoleAsync(userId, sessionId, http.RequestAborted);
        if (rol is null)
            return ApiErrors.Result(ErrorCodes.NoAutenticado);
        if (rol != RoleNames.Superadmin)
            return ApiErrors.Result(ErrorCodes.SinPermiso, SinPermisoMensaje);

        return await next(ctx);
    }
}
