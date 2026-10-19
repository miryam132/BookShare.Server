using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BookShare.Infrastructure.Repositories;
public sealed class LoanRepository(BookShareDbContext context) : ILoanRepository
{
    public void Add(Loan loan) => context.Loans.Add(loan);
    public Task<Loan?> FindByIdAsync(Guid id, bool tracking, CancellationToken ct)
        => (tracking ? context.Loans : context.Loans.AsNoTracking()).Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> HasHistoryAsync(Guid bookId, CancellationToken ct) => context.Loans.AnyAsync(x => x.BookId == bookId, ct);
    public Task<bool> IsActiveBorrowerAsync(Guid bookId, Guid userId, CancellationToken ct)
        => context.Loans.AnyAsync(x => x.BookId == bookId && x.BorrowerId == userId && x.ReturnedAtUtc == null, ct);
    public async Task<PagedResult<Loan>> ListAsync(LoanQuery query, Actor actor, CancellationToken ct)
    {
        var rows = context.Loans.AsNoTracking().AsQueryable();
        if (!actor.IsAdmin) rows = rows.Where(x => x.Book.OwnerId == actor.UserId || x.BorrowerId == actor.UserId);
        if (query.Side == "owner") rows = rows.Where(x => x.Book.OwnerId == actor.UserId);
        if (query.Side == "borrower") rows = rows.Where(x => x.BorrowerId == actor.UserId);
        if (query.Status == "active") rows = rows.Where(x => x.ReturnedAtUtc == null);
        if (query.Status == "returned") rows = rows.Where(x => x.ReturnedAtUtc != null);
        if (query.Status == "overdue") { var now = DateTime.UtcNow; rows = rows.Where(x => x.ReturnedAtUtc == null && x.DueAtUtc < now); }
        var count = await rows.CountAsync(ct);
        var offset = ((long)query.PageNumber - 1) * query.PageSize;
        var items = offset > int.MaxValue ? new List<Loan>() : await rows.OrderByDescending(x => x.BorrowedAtUtc).ThenBy(x => x.Id).Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        return new(items, count, query.PageNumber, query.PageSize);
    }
}
