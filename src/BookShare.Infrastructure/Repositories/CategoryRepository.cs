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

    public Task<Category?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Category?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken)
        => context.Categories.SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, cancellationToken);

    public Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken)
        => context.BookCategories.AnyAsync(x => x.CategoryId == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => await context.Categories.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);

    public void Add(Category category) => context.Categories.Add(category);
    public void Remove(Category category) => context.Categories.Remove(category);
}
