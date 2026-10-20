using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
namespace BookShare.Application.Services;
public sealed class CategoryService(ICategoryRepository categories, IUnitOfWork unit, IMapper mapper) : ICategoryService
{
    private static void Admin(Actor actor)
    {
        if (!actor.IsAdmin) throw new AppException(403, "forbidden", "Only an administrator can manage categories.");
    }
    private async Task<Category> Load(Guid id, CancellationToken ct, bool tracking = false) => await categories.FindByIdAsync(id, ct, tracking)
        ?? throw new AppException(404, "category_not_found", "Category was not found.");
    private async Task Apply(Category category, CategoryRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new AppException(400, "validation_error", "Invalid category name.");
        var normalized = name.ToUpperInvariant();
        var existing = await categories.FindByNormalizedNameAsync(normalized, ct);
        if (existing is not null && existing.Id != category.Id) throw new AppException(409, "category_conflict", "Category already exists.");
        category.Name = name; category.NormalizedName = normalized;
    }
    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(CancellationToken ct) => mapper.Map<List<CategoryResponse>>(await categories.ListAsync(ct));
    public async Task<CategoryResponse> GetAsync(Guid id, CancellationToken ct) => mapper.Map<CategoryResponse>(await Load(id, ct));
    public async Task<CategoryResponse> CreateAsync(CategoryRequest request, Actor actor, CancellationToken ct)
    {
        Admin(actor); var category = new Category(); await Apply(category, request, ct);
        categories.Add(category); await unit.SaveChangesAsync(ct); return mapper.Map<CategoryResponse>(category);
    }
    public async Task<CategoryResponse> UpdateAsync(Guid id, CategoryRequest request, Actor actor, CancellationToken ct)
    {
        Admin(actor); var category = await Load(id, ct, tracking: true); await Apply(category, request, ct);
        await unit.SaveChangesAsync(ct); return mapper.Map<CategoryResponse>(category);
    }
    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct)
    {
        Admin(actor); var category = await Load(id, ct, tracking: true);
        if (await categories.IsAssignedAsync(id, ct)) throw new AppException(409, "category_assigned", "Remove category assignments first.");
        categories.Remove(category); await unit.SaveChangesAsync(ct);
    }
}
