using System.Security.Claims;
using BookShare.Application.DTOs;
using BookShare.Application.Services;
namespace BookShare.API.Controllers;
internal static class ActorIdentity
{
    public static Actor? Optional(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Required(user) : null;
    public static Actor Required(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("sub"), out var id)
        ? new Actor(id, user.IsInRole("Admin")) : throw new AppException(401, "invalid_credentials", "Invalid user identity.");
}
