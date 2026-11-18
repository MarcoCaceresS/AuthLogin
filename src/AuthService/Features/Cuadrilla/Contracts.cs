namespace AuthService.Features.Cuadrilla;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record FuncionarioOut(Guid Id, string Email, string Nombre, string Rol, bool Activo);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiraEnSegundos, FuncionarioOut Funcionario);

/// <summary>Token si salió bien; si no, el código de error del contrato.</summary>
public sealed record SessionResult(TokenResponse? Token, string? ErrorCode)
{
    public static SessionResult Fail(string codigo) => new(null, codigo);
}
