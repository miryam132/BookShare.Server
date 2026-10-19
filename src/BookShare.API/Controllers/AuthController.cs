using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.API.Controllers;

[ApiController]
[Route("api/auth/tokens")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => Ok(await auth.LoginAsync(request, cancellationToken));
}
