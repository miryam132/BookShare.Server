namespace BookShare.API.Middleware;

public sealed record ApiError(int Status, string Code, string Message, string CorrelationId,
    IReadOnlyDictionary<string, string[]>? Errors = null);
