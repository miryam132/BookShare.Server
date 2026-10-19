using BookShare.Application.DTOs;
namespace BookShare.Application.Interfaces;
public interface ILoanService
{
    Task<LoanResponse> CreateAsync(CreateLoanRequest request, Actor actor, CancellationToken ct);
    Task<LoanResponse> GetAsync(Guid id, Actor actor, CancellationToken ct);
    Task<PagedResult<LoanResponse>> ListAsync(LoanQuery query, Actor actor, CancellationToken ct);
    Task<LoanResponse> ReturnAsync(Guid id, Actor actor, CancellationToken ct);
}
