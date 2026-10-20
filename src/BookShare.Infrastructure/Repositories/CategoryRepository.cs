using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Infrastructure.Repositories;

public sealed class CategoryRepository(BookShareDbContext context) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
        => await context.Categories.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public Task<Category?> FindByIdAsync(Guid id, CancellationToken cancellationToken, bool tracking = false)
        => (tracking ? context.Categories : context.Categories.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Category?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken)
        => context.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, cancellationToken);

    public Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken)
        => context.BookCategories.AnyAsync(x => x.CategoryId == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        // These entities join an edited/new book graph and must remain tracked.
        => await context.Categories.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);

    public void Add(Category category) => context.Categories.Add(category);
    public void Remove(Category category) => context.Categories.Remove(category);
}
