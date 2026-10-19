using BookShare.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Infrastructure.Persistence;

internal sealed class DatabaseHealthCheck(BookShareDbContext context) : IDatabaseHealthCheck
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        context.Database.CanConnectAsync(cancellationToken);
}
