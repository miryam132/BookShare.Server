using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Infrastructure.Repositories;

public sealed class BookRepository(BookShareDbContext context) : IBookRepository
{
    public Task<Book?> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        IQueryable<Book> query = context.Books;
        if (!trackChanges) query = query.AsNoTracking();
        return query.Include(x => x.BookCategories).ThenInclude(x => x.Category)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<BookShare.Application.DTOs.PagedResult<Book>> ListAsync(BookShare.Application.DTOs.BookQuery query, Guid? ownerId, CancellationToken cancellationToken)
    {
        var books = context.Books.AsNoTracking().AsQueryable();
        books = ownerId.HasValue ? books.Where(x => x.OwnerId == ownerId.Value)
            : books.Where(x => x.Status != BookShare.Domain.Enums.BookStatus.Hidden);
        if (query.AvailableOnly) books = books.Where(x => x.Status == BookShare.Domain.Enums.BookStatus.Available);
        if (query.CategoryId.HasValue) books = books.Where(x => x.BookCategories.Any(c => c.CategoryId == query.CategoryId.Value));
        if (!string.IsNullOrWhiteSpace(query.City)) { var city = query.City.Trim().ToUpper(); books = books.Where(x => x.City.ToUpper() == city); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpper();
            books = books.Where(x => x.Title.ToUpper().Contains(search) || x.Author.ToUpper().Contains(search) || x.Description.ToUpper().Contains(search));
        }
        var count = await books.CountAsync(cancellationToken);
        var sorted = query.SortBy switch
        {
            "title" => books.OrderBy(x => x.Title).ThenBy(x => x.Id),
            "title_desc" => books.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
            "date" => books.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => books.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
        var offset = ((long)query.PageNumber - 1) * query.PageSize;
        var items = offset > int.MaxValue ? new List<Book>() : await sorted.Skip((int)offset).Take(query.PageSize)
            .Include(x => x.BookCategories).ThenInclude(x => x.Category).ToListAsync(cancellationToken);
        return new(items, count, query.PageNumber, query.PageSize);
    }

    public void Add(Book book) => context.Books.Add(book);
    public void Remove(Book book) => context.Books.Remove(book);
}
