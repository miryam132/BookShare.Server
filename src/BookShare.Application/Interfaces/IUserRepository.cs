using BookShare.Domain.Entities;

namespace BookShare.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken, bool tracking = false);
    void Add(User user);
}
