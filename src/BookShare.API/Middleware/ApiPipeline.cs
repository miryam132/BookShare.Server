using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BookShare.API.Middleware;

public static class ApiPipeline
{
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = ErrorResponseFactory.InvalidModelState;
        });
        return services;
    }

    public static WebApplication UseApiErrors(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            var status = context.Response.StatusCode;
            await context.Response.WriteAsJsonAsync(new ApiError(status, $"http_{status}",
                ReasonPhrases.GetReasonPhrase(status), context.TraceIdentifier));
        });
        return app;
    }
}
