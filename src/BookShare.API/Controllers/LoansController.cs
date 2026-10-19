using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BookShare.API.Controllers;
[ApiController, Authorize, Route("api/loans"), Produces("application/json")]
[ProducesResponseType(typeof(ApiError), 400)]
[ProducesResponseType(typeof(ApiError), 401)]
[ProducesResponseType(typeof(ApiError), 403)]
[ProducesResponseType(typeof(ApiError), 404)]
[ProducesResponseType(typeof(ApiError), 409)]
public sealed class LoansController(ILoanService loans) : ControllerBase
{
    [HttpPost, ProducesResponseType(typeof(LoanResponse), 201)]
    public async Task<ActionResult<LoanResponse>> Create([FromBody] CreateLoanRequest request, CancellationToken ct)
    {
        var loan = await loans.CreateAsync(request, ActorIdentity.Required(User), ct);
        return CreatedAtAction(nameof(Get), new { id = loan.Id }, loan);
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<LoanResponse>>> List([FromQuery] LoanQuery query, CancellationToken ct)
        => Ok(await loans.ListAsync(query, ActorIdentity.Required(User), ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LoanResponse>> Get([FromRoute] Guid id, CancellationToken ct)
        => Ok(await loans.GetAsync(id, ActorIdentity.Required(User), ct));
    [HttpPut("{id:guid}/return")]
    public async Task<ActionResult<LoanResponse>> Return([FromRoute] Guid id, CancellationToken ct)
        => Ok(await loans.ReturnAsync(id, ActorIdentity.Required(User), ct));
}
