using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AutoMapper;
using BookShare.API.Middleware;
using BookShare.Application;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Offline executable checks. Run: dotnet run --project tests/BookShare.IntegrationTests
if (args.Contains("--client"))
{
    try { await ClientChecks.RunAsync(); }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Client verification failed ({exception.GetType().Name}); no credentials are logged.");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Contains("--postgres") || args.Contains("--seed"))
{
    try { await PostgreSqlChecks.RunAsync(args.Contains("--seed")); }
    catch (Exception exception)
    {
        // Never print connection strings or provider exception messages.
        Console.Error.WriteLine($"PostgreSQL verification failed ({exception.GetType().Name}). See the named check above; no credentials are logged.");
        Environment.ExitCode = 1;
    }
    return;
}
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var services = new ServiceCollection();
services.AddLogging();
services.AddRouting();
services.AddApplication();
services.AddBookShareSecurityForTests();
services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> { ["ConnectionStrings:BookShare"] = "Host=localhost;Database=unused;Username=unused" }).Build());
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = provider.CreateScope();
var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
mapper.ConfigurationProvider.AssertConfigurationIsValid();
checks++;
var book = new Book { Title = "Example", Owner = new User { Email = "private@example.com" } };
book.BookCategories.Add(new BookCategory { Book = book, Category = new Category { Name = "History" } });
var mapped = mapper.Map<BookResponse>(book);
Check(mapped.Categories.Single().Name == "History", "Book category mapping failed.");
Check(!JsonSerializer.Serialize(mapped).Contains("private@example.com"), "Book DTO exposed contact details.");
Check(!JsonSerializer.Serialize(mapper.Map<UserResponse>(new User { PasswordHash = "PRIVATE_HASH" })).Contains("PRIVATE_HASH"), "User DTO exposed a password hash.");
var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
Check(ReferenceEquals(unit, scope.ServiceProvider.GetRequiredService<IUnitOfWork>()), "Unit of work must be scoped.");
using (var second = provider.CreateScope())
    Check(!ReferenceEquals(unit, second.ServiceProvider.GetRequiredService<IUnitOfWork>()), "Units of work leaked across scopes.");
Check(scope.ServiceProvider.GetRequiredService<IUserRepository>() is not null, "User repository missing.");
Check(scope.ServiceProvider.GetRequiredService<IBookRepository>() is not null, "Book repository missing.");
Check(scope.ServiceProvider.GetRequiredService<ICategoryRepository>() is not null, "Category repository missing.");

var request = new RegisterUserRequest { Email = "invalid" };
var validation = new List<ValidationResult>();
Check(!Validator.TryValidateObject(request, new ValidationContext(request), validation, true), "Invalid registration accepted.");
var modelState = new ModelStateDictionary();
foreach (var result in validation)
    foreach (var member in result.MemberNames)
        modelState.AddModelError(member, result.ErrorMessage!);
var http = new DefaultHttpContext { TraceIdentifier = "validation-correlation" };
var action = new ActionContext(http, new RouteData(), new ActionDescriptor(), modelState);
var badRequest = (BadRequestObjectResult)ErrorResponseFactory.InvalidModelState(action);
var validationError = (ApiError)badRequest.Value!;
Check(badRequest.StatusCode == 400 && validationError.Code == "validation_error", "Validation contract failed.");
Check(validationError.Errors!.ContainsKey("Email") && validationError.Errors.ContainsKey("Password"), "Validation details missing.");
Check(validationError.CorrelationId == http.TraceIdentifier, "Validation correlation ID missing.");

async Task<DefaultHttpContext> InvokePipeline(bool fail)
{
    var context = new DefaultHttpContext();
    context.RequestServices = provider;
    context.Request.Headers["X-Correlation-ID"] = "untrusted";
    context.Response.Body = new MemoryStream();
    var requestMiddleware = new RequestLoggingMiddleware(_ => fail
        ? throw new InvalidOperationException("SENSITIVE_TEST_VALUE")
        : Task.CompletedTask, NullLogger<RequestLoggingMiddleware>.Instance);
    var exceptionMiddleware = new ExceptionHandlingMiddleware(requestMiddleware.InvokeAsync,
        NullLogger<ExceptionHandlingMiddleware>.Instance);
    await exceptionMiddleware.InvokeAsync(context);
    return context;
}
var failure = await InvokePipeline(true);
Check(failure.Response.StatusCode == 500, "Unhandled exception did not return 500.");
failure.Response.Body.Position = 0;
var body = await new StreamReader(failure.Response.Body).ReadToEndAsync();
var error = JsonSerializer.Deserialize<ApiError>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
Check(error.Status == 500 && error.Code == "server_error", "Server error contract failed.");
Check(!body.Contains("SENSITIVE_TEST_VALUE"), "Exception details leaked into the response.");
Check(error.CorrelationId == failure.Response.Headers["X-Correlation-ID"], "Server error correlation ID mismatch.");
var success = await InvokePipeline(false);
Check(success.Response.StatusCode == 200, "Success response changed.");
Check(success.TraceIdentifier != failure.TraceIdentifier && success.TraceIdentifier != "untrusted", "Correlation IDs are not unique server IDs.");
// Exercise real HTTP model binding and response serialization with the production
// pipeline. Probe endpoints belong exclusively to this executable test assembly.
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Services.AddApiControllers();
builder.Services.AddControllers().AddApplicationPart(typeof(PipelineProbeController).Assembly);
await using var app = builder.Build();
app.UseApiErrors();
app.MapControllers();
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
try
{
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    using var invalid = await client.PostAsJsonAsync("/_test/pipeline", new { Email = "invalid" });
    Check((int)invalid.StatusCode == 400, "HTTP validation did not return 400.");
    var invalidError = (await invalid.Content.ReadFromJsonAsync<ApiError>())!;
    Check(invalidError.Code == "validation_error" && invalidError.Errors!.ContainsKey("Password"), "HTTP validation contract failed.");
    Check(invalid.Headers.GetValues("X-Correlation-ID").Single() == invalidError.CorrelationId, "HTTP validation correlation mismatch.");
    foreach (var (path, status, code) in new[] {
        ("/_test/pipeline/exception", 500, "server_error"),
        ("/missing", 404, "http_404"),
        ("/_test/pipeline/conflict", 409, "http_409") })
    {
        using var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        var responseError = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        Check((int)response.StatusCode == status && responseError.Code == code && responseError.Status == status,
            $"HTTP error contract failed for {path}.");
        Check(response.Headers.GetValues("X-Correlation-ID").Single() == responseError.CorrelationId,
            $"HTTP correlation mismatch for {path}.");
        Check(!text.Contains("SENSITIVE_TEST_VALUE"), "HTTP response leaked exception details.");
    }
}
finally { await app.StopAsync(); }
Console.WriteLine($"Passed {checks} infrastructure and HTTP checks. No database credentials or external test packages required.");
await AuthChecks.RunAsync();
await LoanInfrastructureChecks.RunAsync();
await LoanTransactionChecks.RunAsync();

[ApiController]
[Route("_test/pipeline")]
public sealed class PipelineProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Validate([FromBody] RegisterUserRequest request) => Ok();

    [HttpGet("exception")]
    public IActionResult Fail() => throw new InvalidOperationException("SENSITIVE_TEST_VALUE");

    [HttpGet("conflict")]
    public IActionResult ConflictProbe() => Conflict();
}
