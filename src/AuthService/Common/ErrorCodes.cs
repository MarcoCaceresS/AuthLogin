namespace AuthService.Common;

public static class ErrorCodes
{
    public const string CredencialesInvalidas = "credenciales_invalidas";
    public const string NoAutenticado = "no_autenticado";
    public const string SesionExpirada = "sesion_expirada";
    public const string CuentaInactiva = "cuenta_inactiva";
    public const string SinPermiso = "sin_permiso";
    public const string DemasiadosIntentos = "demasiados_intentos";
    public const string DatosInvalidos = "datos_invalidos";
    public const string EmailEnUso = "email_en_uso";
    public const string UsuarioNoEncontrado = "usuario_no_encontrado";
    public const string ErrorInterno = "error_interno";
}
