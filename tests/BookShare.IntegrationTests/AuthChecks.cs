using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BookShare.API.Controllers;
using BookShare.API.Middleware;
using BookShare.Application;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

public static class AuthChecks
{
    public static IServiceCollection AddBookShareSecurityForTests(this IServiceCollection services)
        => services.AddBookShareSecurity(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test-client",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build());

    public static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly)
            .AddApplicationPart(typeof(AuthProbeController).Assembly);
        builder.Services.AddApplication();
        builder.Services.AddBookShareSecurityForTests();
        builder.Services.AddSingleton<IUserRepository, MemoryUsers>();
        builder.Services.AddSingleton<IBookRepository, MemoryBooks>();
        builder.Services.AddSingleton<ILoanRepository, MemoryLoans>();
        builder.Services.AddSingleton<ICategoryRepository, MemoryCategories>();
        builder.Services.AddScoped<IUnitOfWork, MemoryUnit>();
        await using var app = builder.Build();
        app.UseApiErrors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var input = new { FullName = "Test User", Email = "Reader@example.com", Password = "Test-only-pass123!", ContactPhone = "0501234567", Role = "Admin" };
            using var registration = await client.PostAsJsonAsync("/api/users", input);
            Check((int)registration.StatusCode == 201 && registration.Headers.Location?.ToString() == "/api/users/me", "Registration failed.");
            var user = (await registration.Content.ReadFromJsonAsync<UserResponse>())!;
            Check(user.Role == BookShare.Domain.Enums.UserRole.Member, "Client elevated role.");
            Check(!(await registration.Content.ReadAsStringAsync()).Contains("password", StringComparison.OrdinalIgnoreCase), "Password leaked.");
            var stored = (await app.Services.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, default))!;
            Check(stored.PasswordHash != input.Password && stored.NormalizedEmail == "READER@EXAMPLE.COM", "Hash or normalization failed.");
            var passwordSecurity = new PasswordSecurity();
            Check(passwordSecurity.Verify(stored, input.Password, out _) && !passwordSecurity.Verify(stored, "wrong", out _), "Password verification failed.");
            using var duplicate = await client.PostAsJsonAsync("/api/users", new { input.FullName, Email = "reader@EXAMPLE.com", input.Password, input.ContactPhone });
            Check((int)duplicate.StatusCode == 409 && (await duplicate.Content.ReadFromJsonAsync<ApiError>())!.Code == "email_conflict", "Duplicate not rejected.");
            foreach (var email in new[] { input.Email, "missing@example.com" })
            {
                using var wrong = await client.PostAsJsonAsync("/api/auth/tokens", new { Email = email, Password = "wrong" });
                Check((int)wrong.StatusCode == 401 && (await wrong.Content.ReadFromJsonAsync<ApiError>())!.Code == "invalid_credentials", "Credentials error differs.");
            }
            using var login = await client.PostAsJsonAsync("/api/auth/tokens", new { Email = "reader@EXAMPLE.COM", input.Password });
            Check((int)login.StatusCode == 200, "Login failed.");
            var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
            Check(token.TokenType == "Bearer" && token.ExpiresAtUtc > DateTime.UtcNow, "Invalid token response.");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var me = await client.GetAsync("/api/users/me");
            Check((int)me.StatusCode == 200 && (await me.Content.ReadFromJsonAsync<UserResponse>())!.Id == user.Id, "Token identity failed.");
            using var forbidden = await client.GetAsync("/_test/auth/admin");
            Check((int)forbidden.StatusCode == 403 && (await forbidden.Content.ReadFromJsonAsync<ApiError>())!.Status == 403, "Member admin access was not 403.");
            await BookChecks.RunAsync(client, app.Services, stored);
            await LoanChecks.RunAsync(client, app.Services, stored);
            var settings = app.Services.GetRequiredService<JwtSettings>();
            string MakeToken(string issuer, string audience, SecurityKey key, bool expired = false)
                => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
                    [new Claim("sub", user.Id.ToString())], DateTime.UtcNow.AddHours(-2),
                    expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(5),
                    new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
            foreach (var invalid in new[] { "garbage", MakeToken(settings.Issuer, settings.Audience, settings.Key, true),
                MakeToken("wrong", settings.Audience, settings.Key), MakeToken(settings.Issuer, "wrong", settings.Key),
                MakeToken(settings.Issuer, settings.Audience, new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))) })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalid);
                using var rejected = await client.GetAsync("/api/users/me");
                var error = (await rejected.Content.ReadFromJsonAsync<ApiError>())!;
                Check((int)rejected.StatusCode == 401 && error.Status == 401 && rejected.Headers.GetValues("X-Correlation-ID").Single() == error.CorrelationId, "Invalid JWT accepted or error envelope missing.");
            }
            client.DefaultRequestHeaders.Authorization = null;
            using var anonymous = await client.GetAsync("/api/users/me");
            Check((int)anonymous.StatusCode == 401, "Anonymous me accepted.");
            using var scope = app.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.LoginAsync(new LoginRequest { Email = "  reader@example.com  ", Password = input.Password }, default);
            checks++;
            var normalizedUser = await auth.RegisterAsync(new RegisterUserRequest { FullName = " Trim ", Email = " trim@example.com ", Password = input.Password, ContactPhone = input.ContactPhone }, default);
            Check(normalizedUser.Email == "trim@example.com", "Registration did not trim email.");
        }
        finally { await app.StopAsync(); }
        Console.WriteLine($"Passed {checks} authentication checks with production controllers, service, PasswordHasher and JWT middleware; in-memory persistence, no live database.");
    }
}

[ApiController, Route("_test/auth")]
public sealed class AuthProbeController : ControllerBase
{
    [Authorize(Roles = "Admin"), HttpGet("admin")]
    public IActionResult Admin() => Ok();
}

public sealed class MemoryUsers : IUserRepository
{
    private readonly List<User> users = [];
    public void Add(User user) => users.Add(user);
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(users.SingleOrDefault(x => x.Id == id));
    public Task<User?> FindByNormalizedEmailAsync(string email, CancellationToken cancellationToken, bool tracking = false) => Task.FromResult(users.SingleOrDefault(x => x.NormalizedEmail == email));
}

public sealed class MemoryUnit(IBookRepository books) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var book in ((MemoryBooks)books).Items) book.Version = Guid.NewGuid();
        return Task.FromResult(1);
    }
    public Task BeginTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CommitTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RollbackTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
