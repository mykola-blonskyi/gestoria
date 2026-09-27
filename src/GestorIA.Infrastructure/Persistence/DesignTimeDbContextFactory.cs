using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestorIA.Infrastructure.Persistence;

// What `dotnet ef migrations add` builds the context with. Writing a migration reads the model and never connects, so the
// connection string names no real server and holds no password.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GestoriaDbContext>
{
    public GestoriaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GestoriaDbContext>().UseNpgsql("Host=localhost;Database=gestoria").Options);
}
