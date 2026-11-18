using Microsoft.AspNetCore.Diagnostics;

namespace AuthService.Common;

/// <summary>Toda respuesta de error sale con {"codigo", "mensaje"}, incluso 404/405 y excepciones.</summary>
public static class ErrorHandling
{
    public const string LoginPath = "/api/v1/cuadrilla/login";
    public const string DatosInvalidosGenerico = "Revisa los datos enviados.";

    public static WebApplication UseApiErrors(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = ctx =>
            {
                // Body JSON mal formado, de tipo incorrecto o query inválida → BadHttpRequestException.
                if (ctx.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException)
                {
                    var mensaje = ctx.Request.Path.StartsWithSegments(LoginPath) ? null : DatosInvalidosGenerico;
                    return ApiErrors.WriteAsync(ctx, ErrorCodes.DatosInvalidos, mensaje);
                }
                return ApiErrors.WriteAsync(ctx, ErrorCodes.ErrorInterno);
            },
            // Un body malo es culpa del cliente: no se registra como error del servidor.
            SuppressDiagnosticsCallback = c => c.Exception is BadHttpRequestException,
        });

        // 404 de ruta inexistente y 405 llegan sin cuerpo: se les pone el formato del contrato.
        app.UseStatusCodePages(ctx => ctx.HttpContext.Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => ApiErrors.WriteAsync(ctx.HttpContext, ErrorCodes.NoEncontrado),
            StatusCodes.Status405MethodNotAllowed => ApiErrors.WriteAsync(ctx.HttpContext, ErrorCodes.MetodoNoPermitido),
            _ => Task.CompletedTask,
        });

        return app;
    }
}
