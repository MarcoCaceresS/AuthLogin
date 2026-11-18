using AuthService.Common;
using AuthService.Data;
using AuthService.Domain;
using AuthService.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthService.Features.Admin;

public sealed class UserAdminService(AuthDbContext db, IPasswordHasher<User> hasher, TimeProvider time)
{
    private static readonly HashSet<string> CreatableRoles = [RoleNames.Funcionario, RoleNames.Cuadrilla];

    public async Task<AdminResult<UsuarioOut>> CreateAsync(CrearUsuarioRequest r, CancellationToken ct)
    {
        var email = User.NormalizeEmail(r.Email ?? "");
        var nombre = r.Nombre?.Trim() ?? "";
        var apellido = r.Apellido?.Trim() ?? "";

        if (email.Length is 0 or > 254 || !email.Contains('@'))
            return Invalid("Ingresa un correo válido.");
        if (nombre.Length is 0 or > 100 || apellido.Length is 0 or > 100)
            return Invalid("Ingresa nombre y apellido.");
        if (PasswordRules.Validate(r.Password) is { } problema)
            return Invalid(problema);
        if (r.Rol is null || !CreatableRoles.Contains(r.Rol))
            return Invalid("El rol debe ser funcionario o cuadrilla.");

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return AdminResult<UsuarioOut>.Fail(ErrorCodes.EmailEnUso);

        var role = await db.Roles.SingleAsync(x => x.Name == r.Rol, ct);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = nombre,
            LastName = apellido,
            IsActive = true,
            IsEmailVerified = true,
            CreatedAt = time.GetUtcNow(),
            RoleId = role.Id,
            Role = role,
        };
        user.PasswordHash = hasher.HashPassword(user, r.Password!);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos altas simultáneas con el mismo correo.
            return AdminResult<UsuarioOut>.Fail(ErrorCodes.EmailEnUso);
        }

        return AdminResult<UsuarioOut>.Ok(ToOut(user));
    }

    public async Task<IReadOnlyList<UsuarioOut>> ListAsync(bool? activo, string? rol, CancellationToken ct)
    {
        var query = db.Users.Include(u => u.Role).AsNoTracking();
        if (activo is not null)
            query = query.Where(u => u.IsActive == activo);
        if (!string.IsNullOrWhiteSpace(rol))
            query = query.Where(u => u.Role.Name == rol);

        var users = await query.OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return users.Select(ToOut).ToList();
    }

    private static UsuarioOut ToOut(User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.Role.Name, u.IsActive, u.CreatedAt);

    private static AdminResult<UsuarioOut> Invalid(string mensaje) =>
        AdminResult<UsuarioOut>.Fail(ErrorCodes.DatosInvalidos, mensaje);
}
