using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Infrastructure.Repositories;

public sealed class UserRepository(BookShareDbContext context) : IUserRepository
{
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken, bool tracking = false)
        => (tracking ? context.Users : context.Users.AsNoTracking())
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public void Add(User user) => context.Users.Add(user);
}
