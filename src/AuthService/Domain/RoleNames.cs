namespace AuthService.Domain;

public static class RoleNames
{
    public const string Funcionario = "funcionario";
    public const string Cuadrilla = "cuadrilla";
    public const string Superadmin = "superadmin";

    /// <summary>Roles que existen en este servicio. Un token con otro rol no es válido aquí.</summary>
    public static readonly IReadOnlySet<string> AppFuncionarios =
        new HashSet<string> { Funcionario, Cuadrilla, Superadmin };
}
