using System.Diagnostics;
using System.Security.Cryptography;
using BookShare.API.Controllers;
using BookShare.API.Middleware;
using BookShare.Application;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using BookShare.Infrastructure;
using BookShare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

// Test-only host: production controllers/services/security/repositories and real PostgreSQL.
// No demo seed, production endpoints or application database access.
public static class ClientChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__BookShare");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException();
        var cs = new NpgsqlConnectionStringBuilder(connection);
        if (cs.Database is null || !System.Text.RegularExpressions.Regex.IsMatch(cs.Database, "^bookshare_test_[a-z0-9_]+$"))
            throw new InvalidOperationException();
        cs.IncludeErrorDetail = false;
        await using (var raw = new NpgsqlConnection(cs.ConnectionString))
        {
            await raw.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')", raw);
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 0) throw new InvalidOperationException();
        }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        builder.Services.AddApplication();
        builder.Services.AddBookShareSecurityForTests();
        builder.Services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:BookShare"] = cs.ConnectionString }).Build());
        await using var app = builder.Build();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookShareDbContext>();
            await db.Database.MigrateAsync();
            var admin = new User { Id = Guid.NewGuid(), FullName = "Test administrator", Email = "admin@client.test", NormalizedEmail = "ADMIN@CLIENT.TEST", ContactPhone = "0501234567", Role = UserRole.Admin };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
        app.UseApiErrors(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var clientDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../BookShare.Client"));
            if (!File.Exists(Path.Combine(clientDirectory, "package.json"))) throw new InvalidOperationException();
            var start = new ProcessStartInfo("cmd.exe") { WorkingDirectory = clientDirectory, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("npm.cmd test -- --project=live");
            start.Environment["API_PROXY_TARGET"] = app.Urls.Single();
            start.Environment["BOOKSHARE_LIVE_TEST"] = "1";
            start.Environment["BOOKSHARE_TEST_ADMIN_PASSWORD"] = password;
            // Do not pass database credentials into the browser test process.
            start.Environment.Remove("ConnectionStrings__BookShare"); start.Environment.Remove("PGPASSWORD");
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (process.ExitCode != 0) throw new InvalidOperationException();
            // A skipped browser run must never count as an integration pass.
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BookShareDbContext>();
                if (await db.Users.CountAsync() != 3 || await db.Categories.CountAsync() != 1 ||
                    await db.Books.CountAsync() != 1 || await db.Loans.CountAsync() != 1 ||
                    !await db.Loans.AllAsync(x => x.ReturnedAtUtc != null) ||
                    !await db.Books.AllAsync(x => x.Status == BookStatus.Available && x.Title == "ספר הבדיקה מעודכן"))
                    throw new InvalidOperationException();
            }
            Console.WriteLine("PASS: React browser workflow against production API components and isolated PostgreSQL.");
        }
        finally { await app.StopAsync(); }
    }
}
