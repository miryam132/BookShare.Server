using BookShare.Domain.Entities;

namespace BookShare.Application.Interfaces;

public interface IBookRepository
{
    Task<Book?> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<BookShare.Application.DTOs.PagedResult<Book>> ListAsync(BookShare.Application.DTOs.BookQuery query, Guid? ownerId, CancellationToken cancellationToken);
    void Add(Book book);
    void Remove(Book book);
}
