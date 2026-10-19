using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
namespace BookShare.Application.Services;

public sealed class BookService(IBookRepository books, ICategoryRepository categories,
    IUserRepository users, ILoanRepository loans, IUnitOfWork unit, IMapper mapper) : IBookService
{
    private static bool CanManage(Book book, Actor? actor) => actor is not null && (actor.IsAdmin || actor.UserId == book.OwnerId);
    private async Task<Book> Load(Guid id, Actor? actor, bool track, CancellationToken ct)
    {
        var book = await books.FindByIdAsync(id, track, ct);
        if (book is null || (book.Status == BookStatus.Hidden && !CanManage(book, actor)))
            throw new AppException(404, "book_not_found", "Book was not found.");
        return book;
    }
    private static void Manage(Book book, Actor actor)
    {
        if (!CanManage(book, actor)) throw new AppException(403, "forbidden", "You cannot manage this book.");
        if (book.Status == BookStatus.Loaned) throw new AppException(409, "book_loaned", "A loaned book cannot be changed.");
    }
    public async Task<PagedResult<BookResponse>> ListAsync(BookQuery query, Actor? actor, CancellationToken ct)
    {
        if (query.Mine && actor is null) throw new AppException(401, "authentication_required", "Authentication is required for mine.");
        if (query.PageNumber < 1 || query.PageSize < 1 || query.PageSize > 50 ||
            query.SortBy is not ("title" or "title_desc" or "date" or "date_desc"))
            throw new AppException(400, "validation_error", "Invalid pagination or sorting.");
        var page = await books.ListAsync(query, query.Mine ? actor!.UserId : null, ct);
        return new(mapper.Map<List<BookResponse>>(page.Items), page.TotalCount, page.PageNumber, page.PageSize);
    }
    public async Task<BookResponse> GetAsync(Guid id, Actor? actor, CancellationToken ct)
        => mapper.Map<BookResponse>(await Load(id, actor, false, ct));
    public async Task<BookContactResponse> ContactAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var book = await Load(id, actor, false, ct);
        if (book.Status == BookStatus.Loaned && !CanManage(book, actor) && !await loans.IsActiveBorrowerAsync(id, actor.UserId, ct))
            throw new AppException(403, "forbidden", "Contact is restricted to active loan participants.");
        var owner = await users.FindByIdAsync(book.OwnerId, ct) ?? throw new AppException(404, "user_not_found", "Owner was not found.");
        return mapper.Map<BookContactResponse>(owner);
    }
    private async Task Apply(Book book, CreateBookRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Author) ||
            string.IsNullOrWhiteSpace(request.Description) || string.IsNullOrWhiteSpace(request.City) || request.CategoryIds is null)
            throw new AppException(400, "validation_error", "Book fields are required.");
        if (request.CategoryIds.Distinct().Count() != request.CategoryIds.Count)
            throw new AppException(400, "invalid_categories", "Category IDs must be distinct.");
        var selected = await categories.FindByIdsAsync(request.CategoryIds, ct);
        if (selected.Count != request.CategoryIds.Count) throw new AppException(400, "invalid_categories", "A category does not exist.");
        book.Title = request.Title.Trim(); book.Author = request.Author.Trim();
        book.Description = request.Description.Trim(); book.City = request.City.Trim();
        foreach (var link in book.BookCategories.Where(x => !request.CategoryIds.Contains(x.CategoryId)).ToList())
            book.BookCategories.Remove(link);
        foreach (var category in selected.Where(x => !book.BookCategories.Any(l => l.CategoryId == x.Id)))
            book.BookCategories.Add(new BookCategory { BookId = book.Id, CategoryId = category.Id, Category = category });
    }
    public async Task<BookResponse> CreateAsync(CreateBookRequest request, Actor actor, CancellationToken ct)
    {
        if (await users.FindByIdAsync(actor.UserId, ct) is null) throw new AppException(401, "invalid_credentials", "User no longer exists.");
        var book = new Book { OwnerId = actor.UserId, Status = BookStatus.Available };
        await Apply(book, request, ct); books.Add(book); await unit.SaveChangesAsync(ct);
        return mapper.Map<BookResponse>(book);
    }
    public async Task<BookResponse> UpdateAsync(Guid id, UpdateBookRequest request, Actor actor, CancellationToken ct)
    {
        var book = await Load(id, actor, true, ct); Manage(book, actor);
        if (request.Status is not (BookStatus.Available or BookStatus.Hidden))
            throw new AppException(400, "invalid_status", "Only Available and Hidden are allowed.");
        if (request.Version == Guid.Empty || request.Version != book.Version)
            throw new AppException(409, "version_conflict", "Book has changed; reload it before editing.");
        await Apply(book, request, ct); book.Status = request.Status; await unit.SaveChangesAsync(ct);
        return mapper.Map<BookResponse>(book);
    }
    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var book = await Load(id, actor, true, ct); Manage(book, actor);
        if (await loans.HasHistoryAsync(id, ct)) throw new AppException(409, "loan_history", "A book with loan history cannot be deleted.");
        books.Remove(book); await unit.SaveChangesAsync(ct);
    }
}
