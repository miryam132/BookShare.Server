using BookShare.Application.DTOs;
namespace BookShare.Application.Interfaces;
public interface IBookService
{
    Task<PagedResult<BookResponse>> ListAsync(BookQuery query, Actor? actor, CancellationToken ct);
    Task<BookResponse> GetAsync(Guid id, Actor? actor, CancellationToken ct);
    Task<BookContactResponse> ContactAsync(Guid id, Actor actor, CancellationToken ct);
    Task<BookResponse> CreateAsync(CreateBookRequest request, Actor actor, CancellationToken ct);
    Task<BookResponse> UpdateAsync(Guid id, UpdateBookRequest request, Actor actor, CancellationToken ct);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct);
    Task<BookResponse> ReserveAsync(Guid id, ReservationRequest request, Actor actor, CancellationToken ct);
    Task<BookResponse> CancelReservationAsync(Guid id, ReservationRequest request, Actor actor, CancellationToken ct);
}
