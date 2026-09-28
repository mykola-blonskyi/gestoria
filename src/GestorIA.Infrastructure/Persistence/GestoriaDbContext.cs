using GestorIA.Domain.Models;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Infrastructure.Persistence;

// PostgreSQL through EF Core with code-first migrations (ADR-0006), in Persistence/Migrations. After a change to the model:
// dotnet ef migrations add <Name> --project src/GestorIA.Infrastructure --output-dir Persistence/Migrations
public sealed class GestoriaDbContext(DbContextOptions<GestoriaDbContext> options) : DbContext(options)
{
    public DbSet<ProfileRow> Profiles => Set<ProfileRow>();

    public DbSet<BankTransactionRow> BankTransactions => Set<BankTransactionRow>();

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

        modelBuilder.Entity<BankTransactionRow>(line =>
        {
            var classes = Enum.GetNames<TransactionClass>();
            line.ToTable("BankTransactions", table => table.HasCheckConstraint(
                "CK_BankTransactions_Class",
                $"\"Class\" IS NULL OR \"Class\" IN ({string.Join(", ", classes.Select(name => $"'{name}'"))})"));
            // The database, not only the import, refuses a statement line stored twice (LineKeys).
            line.HasIndex(t => new { t.ProfileId, t.LineKey }).IsUnique();
            line.HasIndex(t => new { t.ProfileId, t.BookingDate });
            line.Property(t => t.LineKey).HasMaxLength(64).IsFixedLength();
            line.Property(t => t.Class).HasConversion<string>().HasMaxLength(classes.Max(name => name.Length));
            line.HasOne<ProfileRow>().WithMany().HasForeignKey(t => t.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Every money column is numeric(18,6), never a binary float (ADR-0004, ADR-0006).
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<decimal>().HavePrecision(18, 6);
}
