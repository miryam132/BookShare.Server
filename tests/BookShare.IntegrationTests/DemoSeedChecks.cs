using System.Security.Cryptography;
using BookShare.Domain.Enums;
using BookShare.Infrastructure.Persistence;
using BookShare.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

public static class DemoSeedChecks
{
    public static async Task RunAsync(DbContextOptions<BookShareDbContext> options, CancellationToken ct)
    {
        BookShareDbContext Open() => new(options);
        void Require(bool condition) { if (!condition) throw new InvalidOperationException("Demo seed assertion failed."); }
        var secrets = new Dictionary<string, string?> { ["DemoSeed:Enabled"] = "true" };
        foreach (var role in new[] { "Admin", "Owner", "Borrower1", "Borrower2" })
            secrets[$"DemoSeed:{role}Password"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var config = new ConfigurationBuilder().AddInMemoryCollection(secrets).Build();
        async Task Command(string operation, bool confirmed, int expectedExit)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(BookShare.API.Controllers.UsersController).Assembly.Location);
            start.ArgumentList.Add(operation);
            await using var connectionDb = Open();
            start.Environment["ConnectionStrings__BookShare"] = connectionDb.Database.GetConnectionString();
            start.Environment["Maintenance__Database"] = confirmed
                ? new Npgsql.NpgsqlConnectionStringBuilder(connectionDb.Database.GetConnectionString()).Database : "wrong_target";
            foreach (var secret in secrets) start.Environment[secret.Key.Replace(":", "__")] = secret.Value;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var output = await stdout + await stderr;
            Require(process.ExitCode == expectedExit);
            foreach (var secret in secrets.Where(s => s.Key.EndsWith("Password"))) Require(!output.Contains(secret.Value!));
            Require(!output.Contains(connectionDb.Database.GetConnectionString()!));
        }
        Console.WriteLine("CHECK: maintenance CLI rejects wrong target and migrates idempotently without secret output");
        await using (var db = Open()) Require(!(await db.Database.GetAppliedMigrationsAsync(ct)).Any());
        await Command("--maintenance-migrate", false, 1);
        await using (var db = Open()) Require(!(await db.Database.GetAppliedMigrationsAsync(ct)).Any());
        await Command("--maintenance-migrate", true, 0);
        await Command("--maintenance-migrate", true, 0);
        Console.WriteLine("CHECK: empty migrated database, disabled/missing-secret seed rejected without writes");
        await using (var db = Open())
        {
            Require(await db.Users.CountAsync(ct) == 0 && (await db.Database.GetAppliedMigrationsAsync(ct)).Count() == 3);
            foreach (var invalid in new[] { new ConfigurationBuilder().Build(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["DemoSeed:Enabled"] = "true" }).Build() })
            {
                var rejected = false;
                try { await DemoSeed.RunAsync(db, invalid, ct); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected && await db.Users.CountAsync(ct) == 0);
            }
            await DemoSeed.RunAsync(db, config, ct);
        }
        Console.WriteLine("CHECK: four accounts, password hashes, twelve ads, multiple categories, available race book");
        string hash; Guid version;
        await using (var db = Open())
        {
            Require(await db.Users.CountAsync(ct) == 4 && await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct) == 1);
            Require(await db.Books.CountAsync(ct) == 12 && await db.Categories.CountAsync(ct) == 4 && await db.BookCategories.CountAsync(ct) == 18);
            foreach (var user in await db.Users.ToListAsync(ct))
                Require(new PasswordSecurity().Verify(user, secrets[$"DemoSeed:{user.FullName[5..]}Password"]!, out _));
            var book = await db.Books.SingleAsync(b => b.Id == DemoSeed.Id(100), ct);
            Require(book.Status == BookStatus.Available);
            // Simulate later user changes: subsequent seed must preserve these.
            book.Status = BookStatus.Hidden; book.Title = "Preserved demo edit";
            await db.SaveChangesAsync(ct);
            version = book.Version;
            hash = (await db.Users.SingleAsync(u => u.Id == DemoSeed.Id(1), ct)).PasswordHash;
        }
        Console.WriteLine("CHECK: concurrent repeat seeds preserve identities, hashes, status, edits and Versions");
        async Task Seed() { await using var db = Open(); await DemoSeed.RunAsync(db, config, ct); }
        await Task.WhenAll(Seed(), Seed());
        await Command("--maintenance-seed", true, 0);
        await using (var db = Open())
        {
            Require(await db.Users.CountAsync(ct) == 4 && await db.Books.CountAsync(ct) == 12 && await db.BookCategories.CountAsync(ct) == 18);
            Require((await db.Users.SingleAsync(u => u.Id == DemoSeed.Id(1), ct)).PasswordHash == hash);
            var book = await db.Books.SingleAsync(b => b.Id == DemoSeed.Id(100), ct);
            Require(book.Status == BookStatus.Hidden && book.Title == "Preserved demo edit" && book.Version == version);
            // Identity mismatch must fail atomically, never promote an account.
            var admin = await db.Users.SingleAsync(u => u.Id == DemoSeed.Id(1), ct);
            admin.Role = UserRole.Member; await db.SaveChangesAsync(ct);
        }
        await using (var db = Open())
        {
            var rejected = false;
            try { await DemoSeed.RunAsync(db, config, ct); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
        }
        Console.WriteLine("PASSED demo seed: empty database migrations, safe validation, repeat/concurrent runs and preservation. Isolated database only.");
    }
}
