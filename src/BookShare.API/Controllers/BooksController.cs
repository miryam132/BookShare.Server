using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BookShare.API.Controllers;
[Produces("application/json")]
[ProducesResponseType(typeof(BookShare.API.Middleware.ApiError), 400)]
[ProducesResponseType(typeof(BookShare.API.Middleware.ApiError), 401)]
[ProducesResponseType(typeof(BookShare.API.Middleware.ApiError), 403)]
[ProducesResponseType(typeof(BookShare.API.Middleware.ApiError), 404)]
[ProducesResponseType(typeof(BookShare.API.Middleware.ApiError), 409)]
[ApiController, Route("api/books")]
public sealed class BooksController(IBookService books) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<ActionResult<PagedResult<BookResponse>>> List([FromQuery] BookQuery query, CancellationToken ct)
        => Ok(await books.ListAsync(query, ActorIdentity.Optional(User), ct));
    [HttpGet("{id:guid}"), AllowAnonymous]
    public async Task<ActionResult<BookResponse>> Get([FromRoute] Guid id, CancellationToken ct)
        => Ok(await books.GetAsync(id, ActorIdentity.Optional(User), ct));
    [HttpGet("{id:guid}/contact"), Authorize]
    public async Task<ActionResult<BookContactResponse>> Contact([FromRoute] Guid id, CancellationToken ct)
        => Ok(await books.ContactAsync(id, ActorIdentity.Required(User), ct));
    [HttpPost("{id:guid}/reservation"), Authorize]
    public async Task<ActionResult<BookResponse>> Reserve(Guid id, [FromBody] ReservationRequest request, CancellationToken ct)
        => Ok(await books.ReserveAsync(id, request, ActorIdentity.Required(User), ct));
    [HttpGet("{id:guid}/reservation/contact"), Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ReservationContactResponse>> ReservationContact(Guid id, CancellationToken ct)
        => Ok(await books.ReservationContactAsync(id, ActorIdentity.Required(User), ct));
    [HttpDelete("{id:guid}/reservation"), Authorize]
    public async Task<ActionResult<BookResponse>> CancelReservation(Guid id, [FromBody] ReservationRequest request, CancellationToken ct)
        => Ok(await books.CancelReservationAsync(id, request, ActorIdentity.Required(User), ct));
    [ProducesResponseType(typeof(BookResponse), 201)]
    [HttpPost, Authorize]
    public async Task<ActionResult<BookResponse>> Create([FromBody] CreateBookRequest request, CancellationToken ct)
    {
        var book = await books.CreateAsync(request, ActorIdentity.Required(User), ct);
        return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
    }
    [HttpPut("{id:guid}"), Authorize]
    public async Task<ActionResult<BookResponse>> Update([FromRoute] Guid id, [FromBody] UpdateBookRequest request, CancellationToken ct)
        => Ok(await books.UpdateAsync(id, request, ActorIdentity.Required(User), ct));
    [ProducesResponseType(204)]
    [HttpDelete("{id:guid}"), Authorize]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    { await books.DeleteAsync(id, ActorIdentity.Required(User), ct); return NoContent(); }
}
