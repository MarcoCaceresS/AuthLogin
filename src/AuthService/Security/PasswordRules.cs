namespace AuthService.Security;

public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <summary>Devuelve null si la clave es válida, o el mensaje para el usuario.</summary>
    public static string? Validate(string? password) =>
        password is { Length: >= MinLength and <= MaxLength }
            ? null
            : $"La contraseña debe tener entre {MinLength} y {MaxLength} caracteres.";
}
