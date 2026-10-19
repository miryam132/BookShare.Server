namespace BookShare.API.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (BookShare.Application.Services.AppException exception)
        {
            context.Response.StatusCode = exception.Status;
            await context.Response.WriteAsJsonAsync(new ApiError(exception.Status, exception.Code,
                exception.Message, context.TraceIdentifier), context.RequestAborted);
        }
        catch (Exception exception)
        {
            // Messages and exception Data can contain provider SQL or sensitive input.
            logger.LogError("Unhandled {ExceptionType}; CorrelationId={CorrelationId}; Stack={StackTrace}",
                exception.GetType().FullName, context.TraceIdentifier, exception.StackTrace);
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(new ApiError(500, "server_error",
                "An unexpected server error occurred.", context.TraceIdentifier), context.RequestAborted);
        }
    }
}
