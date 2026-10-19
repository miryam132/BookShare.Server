using System.Security.Claims;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.API.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Register([FromBody] RegisterUserRequest request, CancellationToken cancellationToken)
        => Created("/api/users/me", await auth.RegisterAsync(request, cancellationToken));

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId)) return Unauthorized();
        return Ok(await auth.GetUserAsync(userId, cancellationToken));
    }
}
