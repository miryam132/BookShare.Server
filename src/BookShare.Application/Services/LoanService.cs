using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Microsoft.Extensions.Options;
namespace BookShare.Application.Services;
public sealed class LoanService(ILoanRepository loans, IBookRepository books, IUserRepository users,
    IUnitOfWork unit, IMapper mapper, IOptions<LoanOptions> options) : ILoanService
{
    private static void Manage(Book book, Actor actor)
    {
        if (!actor.IsAdmin && book.OwnerId != actor.UserId)
            throw new AppException(403, "forbidden", "Only the owner or Admin may record lending actions.");
    }
    private async Task<Loan> Load(Guid id, bool tracking, CancellationToken ct)
        => await loans.FindByIdAsync(id, tracking, ct) ?? throw new AppException(404, "loan_not_found", "Loan was not found.");
    public async Task<LoanResponse> GetAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var loan = await Load(id, false, ct);
        if (!actor.IsAdmin && loan.BorrowerId != actor.UserId && loan.Book.OwnerId != actor.UserId)
            throw new AppException(403, "forbidden", "You cannot view this loan.");
        return mapper.Map<LoanResponse>(loan);
    }
    public async Task<PagedResult<LoanResponse>> ListAsync(LoanQuery query, Actor actor, CancellationToken ct)
    {
        if (query.PageNumber < 1 || query.PageSize is < 1 or > 50 || query.Side is not ("all" or "owner" or "borrower") || query.Status is not ("all" or "active" or "returned" or "overdue"))
            throw new AppException(400, "validation_error", "Invalid loan filters or pagination.");
        var page = await loans.ListAsync(query, actor, ct);
        return new(mapper.Map<List<LoanResponse>>(page.Items), page.TotalCount, page.PageNumber, page.PageSize);
    }
    public async Task<LoanResponse> CreateAsync(CreateLoanRequest request, Actor actor, CancellationToken ct)
    {
        await unit.BeginTransactionAsync(ct);
        try
        {
            var book = await books.FindByIdAsync(request.BookId, true, ct) ?? throw new AppException(404, "book_not_found", "Book was not found.");
            Manage(book, actor);
            if (book.Status != BookStatus.Available) throw new AppException(409, "book_unavailable", "Book is not available.");
            var borrower = await users.FindByNormalizedEmailAsync(request.BorrowerEmail.Trim().ToUpperInvariant(), ct)
                ?? throw new AppException(400, "borrower_not_found", "Borrower must be registered.");
            if (borrower.Id == book.OwnerId) throw new AppException(400, "self_loan", "The owner cannot borrow their own book.");
            var now = DateTime.UtcNow;
            var loan = new Loan { BookId = book.Id, Book = book, BorrowerId = borrower.Id, RecordedByUserId = actor.UserId,
                BorrowedAtUtc = now, DueAtUtc = now.AddDays(options.Value.DurationDays) };
            loans.Add(loan); book.Status = BookStatus.Loaned;
            await unit.SaveChangesAsync(ct);
            await unit.CommitTransactionAsync(ct);
            return mapper.Map<LoanResponse>(loan);
        }
        catch { await unit.RollbackTransactionAsync(CancellationToken.None); throw; }
    }
    public async Task<LoanResponse> ReturnAsync(Guid id, Actor actor, CancellationToken ct)
    {
        await unit.BeginTransactionAsync(ct);
        try
        {
            var loan = await Load(id, true, ct);
            Manage(loan.Book, actor);
            if (loan.ReturnedAtUtc.HasValue) throw new AppException(409, "already_returned", "Loan has already been returned.");
            if (loan.Book.Status != BookStatus.Loaned) throw new AppException(409, "invalid_book_state", "Book is not loaned.");
            loan.ReturnedAtUtc = DateTime.UtcNow; loan.Book.Status = BookStatus.Available;
            await unit.SaveChangesAsync(ct);
            await unit.CommitTransactionAsync(ct);
            return mapper.Map<LoanResponse>(loan);
        }
        catch { await unit.RollbackTransactionAsync(CancellationToken.None); throw; }
    }
}
