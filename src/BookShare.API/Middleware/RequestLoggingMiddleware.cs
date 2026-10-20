using System.Diagnostics;

namespace BookShare.API.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Generate server-side IDs; untrusted header values never enter the logs.
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.TraceIdentifier
        });
        var timer = Stopwatch.StartNew();
        try { await next(context); }
        catch (BookShare.Application.Services.AppException exception)
        {
            // The outer error handler will serialize this same status. Record it
            // before logging, rather than temporarily misclassifying it as 500.
            if (!context.Response.HasStarted) context.Response.StatusCode = exception.Status;
            throw;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
            throw;
        }
        catch
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 500;
            throw;
        }
        finally
        {
            // No query strings, request bodies, authentication headers or credentials.
            logger.Log(context.Response.StatusCode == 409 ? LogLevel.Warning : LogLevel.Information, "HTTP {Method} {Path} returned {Status} in {ElapsedMs} ms",
                context.Request.Method, context.Request.Path.Value,
                context.Response.StatusCode, timer.ElapsedMilliseconds);
        }
    }
}
