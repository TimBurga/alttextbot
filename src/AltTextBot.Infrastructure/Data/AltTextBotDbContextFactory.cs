using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AltTextBot.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF Core migrations. Used only by dotnet ef CLI.
/// </summary>
public class AltTextBotDbContextFactory : IDesignTimeDbContextFactory<AltTextBotDbContext>
{
    public AltTextBotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AltTextBotDbContext>()
            .UseNpgsql("Host=localhost;Database=alttext_bot_design;Username=postgres;Password=postgres")
            .Options;
        return new AltTextBotDbContext(options);
    }
}
