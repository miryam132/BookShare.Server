using Microsoft.AspNetCore.Mvc;

namespace BookShare.API.Middleware;

public static class ErrorResponseFactory
{
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Invalid value." : error.ErrorMessage).ToArray());
        return new BadRequestObjectResult(new ApiError(400, "validation_error",
            "One or more input values are invalid.", context.HttpContext.TraceIdentifier, errors));
    }
}
