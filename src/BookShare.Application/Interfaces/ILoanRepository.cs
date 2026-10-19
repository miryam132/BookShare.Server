using BookShare.Application.DTOs;
using BookShare.Domain.Entities;
namespace BookShare.Application.Interfaces;
public interface ILoanRepository
{
    Task<Loan?> FindByIdAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> HasHistoryAsync(Guid bookId, CancellationToken ct);
    Task<bool> IsActiveBorrowerAsync(Guid bookId, Guid userId, CancellationToken ct);
    Task<PagedResult<Loan>> ListAsync(LoanQuery query, Actor actor, CancellationToken ct);
    void Add(Loan loan);
}
