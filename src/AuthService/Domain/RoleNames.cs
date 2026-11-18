namespace AuthService.Domain;

public static class RoleNames
{
    public const string Funcionario = "funcionario";
    public const string Cuadrilla = "cuadrilla";
    public const string Superadmin = "superadmin";
    public const string Usuario = "usuario";

    /// <summary>Roles que pueden usar la app de funcionarios.</summary>
    public static readonly IReadOnlySet<string> AppFuncionarios =
        new HashSet<string> { Funcionario, Cuadrilla, Superadmin };
}
