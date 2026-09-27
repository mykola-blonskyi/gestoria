using GestorIA.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Infrastructure.Persistence;

// PostgreSQL through EF Core with code-first migrations (ADR-0006), in Persistence/Migrations. After a change to the model:
// dotnet ef migrations add <Name> --project src/GestorIA.Infrastructure --output-dir Persistence/Migrations
public sealed class GestoriaDbContext(DbContextOptions<GestoriaDbContext> options) : DbContext(options)
{
    public DbSet<ProfileRow> Profiles => Set<ProfileRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileRow>(profile =>
        {
            profile.ToTable("Profiles", table =>
            {
                table.HasCheckConstraint("CK_Profiles_NewActivity", "(\"NewActivityPeriod\" IS NULL) = (\"IngresosFromFormerEmployer\" IS NULL)");
                table.HasCheckConstraint("CK_Profiles_Singleton", "\"Singleton\"");
            });
            profile.HasIndex(p => p.Singleton).IsUnique();
            profile.Property(p => p.Region).HasMaxLength(2);
            profile.Property(p => p.NewActivityPeriod).HasConversion<string>().HasMaxLength(9);
        });
    }

    // Every money column is numeric(18,6), never a binary float (ADR-0004, ADR-0006).
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<decimal>().HavePrecision(18, 6);
}
