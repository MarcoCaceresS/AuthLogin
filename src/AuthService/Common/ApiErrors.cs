namespace AuthService.Common;

public sealed record ErrorBody(string Codigo, string Mensaje);

/// <summary>Respuestas de error con el formato del contrato: {"codigo", "mensaje"}.</summary>
public static class ApiErrors
{
    private static readonly Dictionary<string, (int Status, string Mensaje)> Catalog = new()
    {
        [ErrorCodes.CredencialesInvalidas] = (401, "Correo o contraseña incorrectos."),
        [ErrorCodes.NoAutenticado] = (401, "Debes iniciar sesión."),
        [ErrorCodes.SesionExpirada] = (401, "Tu sesión expiró. Inicia sesión nuevamente."),
        [ErrorCodes.CuentaInactiva] = (401, "Tu cuenta fue desactivada."),
        [ErrorCodes.SinPermiso] = (403, "No tienes permiso para usar esta aplicación."),
        [ErrorCodes.DemasiadosIntentos] = (429, "Demasiados intentos. Espera un momento e inténtalo de nuevo."),
        [ErrorCodes.DatosInvalidos] = (422, "Ingresa tu correo y contraseña."),
        [ErrorCodes.EmailEnUso] = (409, "Ya existe un usuario con ese correo."),
        [ErrorCodes.UsuarioNoEncontrado] = (404, "No se encontró el usuario."),
        [ErrorCodes.NoEncontrado] = (404, "La ruta no existe."),
        [ErrorCodes.MetodoNoPermitido] = (405, "Método no permitido en esta ruta."),
        [ErrorCodes.ErrorInterno] = (500, "Ocurrió un error. Inténtalo más tarde."),
    };

    public static int StatusOf(string codigo) => Catalog[codigo].Status;

    public static IResult Result(string codigo, string? mensaje = null)
    {
        var (status, defecto) = Catalog[codigo];
        return Results.Json(new ErrorBody(codigo, mensaje ?? defecto), statusCode: status);
    }

    public static Task WriteAsync(HttpContext ctx, string codigo, string? mensaje = null) =>
        Result(codigo, mensaje).ExecuteAsync(ctx);
}
