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
[ApiController, Route("api/categories")]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> List(CancellationToken ct) => Ok(await categories.ListAsync(ct));
    [HttpGet("{id:guid}"), AllowAnonymous]
    public async Task<ActionResult<CategoryResponse>> Get([FromRoute] Guid id, CancellationToken ct) => Ok(await categories.GetAsync(id, ct));
    [ProducesResponseType(typeof(CategoryResponse), 201)]
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryResponse>> Create([FromBody] CategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ActorIdentity.Required(User), ct);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }
    [HttpPut("{id:guid}"), Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryResponse>> Update([FromRoute] Guid id, [FromBody] CategoryRequest request, CancellationToken ct)
        => Ok(await categories.UpdateAsync(id, request, ActorIdentity.Required(User), ct));
    [ProducesResponseType(204)]
    [HttpDelete("{id:guid}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    { await categories.DeleteAsync(id, ActorIdentity.Required(User), ct); return NoContent(); }
}
