using BookShare.Application.DTOs;
namespace BookShare.Application.Interfaces;
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> ListAsync(CancellationToken ct);
    Task<CategoryResponse> GetAsync(Guid id, CancellationToken ct);
    Task<CategoryResponse> CreateAsync(CategoryRequest request, Actor actor, CancellationToken ct);
    Task<CategoryResponse> UpdateAsync(Guid id, CategoryRequest request, Actor actor, CancellationToken ct);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct);
}
