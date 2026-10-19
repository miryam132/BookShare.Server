using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookShare.Infrastructure.Persistence;

// Schema generation is offline. Database updates require an explicit connection.
public sealed class BookShareDbContextFactory : IDesignTimeDbContextFactory<BookShareDbContext>
{
    public BookShareDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__BookShare")
            ?? throw new InvalidOperationException("Set ConnectionStrings__BookShare before using EF tools.");
        return new BookShareDbContext(new DbContextOptionsBuilder<BookShareDbContext>()
            .UseNpgsql(connection).Options);
    }
}
