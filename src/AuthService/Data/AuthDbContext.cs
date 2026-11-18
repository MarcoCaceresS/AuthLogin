using AuthService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<LoginRecord> LoginRecords => Set<LoginRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Role>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(32);
            e.HasIndex(r => r.Name).IsUnique();
            e.HasData(
                new Role { Id = 1, Name = RoleNames.Funcionario },
                new Role { Id = 2, Name = RoleNames.Cuadrilla },
                new Role { Id = 3, Name = RoleNames.Superadmin },
                new Role { Id = 4, Name = RoleNames.Usuario });
        });

        model.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(254);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.HasOne(u => u.Role).WithMany().HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<UserSession>(e =>
        {
            e.Property(s => s.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.UserId);
        });

        model.Entity<LoginRecord>(e =>
        {
            e.Property(l => l.IpAddress).HasMaxLength(45);
            e.Property(l => l.UserAgent).HasMaxLength(LoginRecord.UserAgentMaxLength);
            e.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserSession>().WithMany().HasForeignKey(l => l.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => new { l.UserId, l.OccurredAt });
        });
    }
}
