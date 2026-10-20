using BookShare.Domain.Entities;

namespace BookShare.Application.Interfaces;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
    Task<Category?> FindByIdAsync(Guid id, CancellationToken cancellationToken, bool tracking = false);
    Task<Category?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);
    Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void Add(Category category);
    void Remove(Category category);
}
