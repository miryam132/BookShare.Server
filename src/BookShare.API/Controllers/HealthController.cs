using BookShare.API.Middleware;
using BookShare.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.API.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(IDatabaseHealthCheck database) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpGet("database")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetDatabase(CancellationToken cancellationToken)
    {
        var connected = await database.CanConnectAsync(cancellationToken);
        return connected
            ? Ok(new { status = "ok" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiError(503, "database_unavailable", "The database is unavailable.", HttpContext.TraceIdentifier));
    }
}
