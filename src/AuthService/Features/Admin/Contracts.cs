namespace AuthService.Features.Admin;

public sealed record CrearUsuarioRequest(string? Email, string? Password, string? Nombre, string? Apellido, string? Rol);

public sealed record UsuarioOut(
    Guid Id, string Email, string Nombre, string Apellido, string Rol, bool Activo, DateTimeOffset CreadoEn);

/// <summary>Valor si salió bien; si no, código de error y mensaje opcional.</summary>
public sealed record AdminResult<T>(T? Value, string? ErrorCode, string? Mensaje = null)
{
    public static AdminResult<T> Ok(T value) => new(value, null);
    public static AdminResult<T> Fail(string codigo, string? mensaje = null) => new(default, codigo, mensaje);
}
