using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Subconscious.Engine.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add/remove</c> and <c>dotnet ef database
/// update</c> can construct a <see cref="SubconsciousDbContext"/> without booting the full
/// engine host (this project has no real <c>Main</c> — see <c>Program.cs</c>). The connection
/// string here is only ever used by the EF CLI tooling; the running engine always configures
/// its own connection via <see cref="EngineHost.Build"/> against the user's data directory.
/// </summary>
public class SubconsciousDbContextFactory : IDesignTimeDbContextFactory<SubconsciousDbContext>
{
    public SubconsciousDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SubconsciousDbContext>()
            .UseSqlite("Data Source=subconscious.design.db");

        return new SubconsciousDbContext(optionsBuilder.Options);
    }
}
