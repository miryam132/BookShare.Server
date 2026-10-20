using BookShare.API.Middleware;
using BookShare.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public static class LoggingChecks
{
    public static async Task RunAsync()
    {
        foreach (var status in new[] { 400, 401, 403, 404, 409 })
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var logger = new CapturedLogger<RequestLoggingMiddleware>();
            var request = new RequestLoggingMiddleware(_ => throw new AppException(status, "test_error", "Test error."), logger);
            var errors = new ExceptionHandlingMiddleware(request.InvokeAsync, NullLogger<ExceptionHandlingMiddleware>.Instance);
            await errors.InvokeAsync(context);
            var entry = logger.Entries.Single();
            if (context.Response.StatusCode != status || entry.Status != status ||
                entry.Level != (status == 409 ? LogLevel.Warning : LogLevel.Information))
                throw new InvalidOperationException("Business exception log status/level did not match HTTP.");
        }
        Console.WriteLine("Passed 5 business exception logging checks, including thrown 409 at Warning.");
    }

    private sealed class CapturedLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int Status)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = (IEnumerable<KeyValuePair<string, object?>>)(object)state!;
            Entries.Add((level, (int)fields.Single(x => x.Key == "Status").Value!));
        }
    }
}
