using System.Net.Http.Json;
using BookShare.API.Controllers;
using BookShare.API.Middleware;
using BookShare.Application;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using BookShare.Infrastructure;
using BookShare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

public static class PostgreSqlChecks
{
    public static async Task RunAsync(bool seedOnly = false)
    {
        Console.WriteLine("CHECK: isolated test database safety and migrations");
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__BookShare");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException();
        var cs = new NpgsqlConnectionStringBuilder(connection);
        if (cs.Database is null || !System.Text.RegularExpressions.Regex.IsMatch(cs.Database, "^bookshare_test_[a-z0-9_]+$"))
            throw new InvalidOperationException();
        cs.IncludeErrorDetail = false;
        var options = new DbContextOptionsBuilder<BookShareDbContext>().UseNpgsql(cs.ConnectionString).Options;
        BookShareDbContext Open() => new(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        // Require a pre-created EMPTY database. Never drop, truncate or reuse a populated database.
        await using (var raw = new NpgsqlConnection(cs.ConnectionString))
        {
            await raw.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')", raw);
            Require(Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) == 0);
        }
        if (seedOnly)
        {
            await DemoSeedChecks.RunAsync(options, ct);
            return;
        }
        await using (var db = Open()) await db.Database.MigrateAsync(ct);
        var owner = User("owner"); var admin = User("admin"); admin.Role = UserRole.Admin;
        var reader1 = User("reader1"); var reader2 = User("reader2");
        var book = Book(owner); var rollbackBook = Book(owner);
        await using (var db = Open())
        {
            db.AddRange(owner, admin, reader1, reader2, book, rollbackBook);
            for (var i = 0; i < 12; i++) db.Add(Book(owner));
            await db.SaveChangesAsync(ct);
        }

        Console.WriteLine("CHECK: independent Version token, no Loans or unique-index involvement");
        await using (var first = Open())
        await using (var second = Open())
        {
            var a = await first.Books.SingleAsync(x => x.Id == book.Id, ct);
            var b = await second.Books.SingleAsync(x => x.Id == book.Id, ct);
            Require(a.Version == b.Version);
            a.Title = "first"; b.Title = "second";
            await first.SaveChangesAsync(ct);
            try { await second.SaveChangesAsync(ct); throw new InvalidOperationException(); }
            catch (DbUpdateConcurrencyException) { }
        }

        var hook = new SaveHook();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // Do not log SQL, connection strings or provider errors.
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        builder.Services.AddApplication();
        builder.Services.AddBookShareSecurityForTests();
        builder.Services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:BookShare"] = cs.ConnectionString }).Build());
        builder.Services.AddDbContext<BookShareDbContext>(o => o.AddInterceptors(hook));
        await using var app = builder.Build();
        app.UseApiErrors(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(40) };
            async Task<HttpResponseMessage> Send(HttpMethod method, string path, User? actor, object? body = null)
            {
                using var message = new HttpRequestMessage(method, path);
                if (actor is not null)
                {
                    using var scope = app.Services.CreateScope();
                    message.Headers.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<ITokenIssuer>().Issue(actor).AccessToken);
                }
                if (body is not null) message.Content = JsonContent.Create(body);
                return await client.SendAsync(message, ct);
            }
            async Task Status(HttpMethod method, string path, User? actor, int expected, object? body = null)
            { using var response = await Send(method, path, actor, body); Require((int)response.StatusCode == expected); }
            object Input(Guid id, User reader) => new { BookId = id, BorrowerEmail = reader.Email };

            Console.WriteLine("CHECK: PostgreSQL API 401/403 and SQL catalog pagination");
            await Status(HttpMethod.Post, "/api/loans", null, 401, Input(book.Id, reader1));
            await Status(HttpMethod.Post, "/api/loans", reader1, 403, Input(book.Id, reader2));
            await Status(HttpMethod.Get, "/api/loans", null, 401);
            using var page1 = await Send(HttpMethod.Get, "/api/books?sortBy=title&pageSize=5", null);
            using var page2 = await Send(HttpMethod.Get, "/api/books?sortBy=title&pageSize=5&pageNumber=2", null);
            var p1 = (await page1.Content.ReadFromJsonAsync<PagedResult<BookResponse>>(ct))!;
            var p2 = (await page2.Content.ReadFromJsonAsync<PagedResult<BookResponse>>(ct))!;
            Require(p1.TotalCount == 14 && p2.TotalCount == 14 && p1.Items.Count == 5 && p2.Items.Count == 5);
            Require(!p1.Items.Select(x => x.Id).Intersect(p2.Items.Select(x => x.Id)).Any());

            Console.WriteLine("CHECK: coordinated borrower reservation race before SQL save");
            Guid reservationVersion;
            await using (var db = Open()) reservationVersion = (await db.Books.SingleAsync(x => x.Id == rollbackBook.Id, ct)).Version;
            var reservePath = $"/api/books/{rollbackBook.Id}/reservation";
            var reservationInput = new ReservationRequest { Version = reservationVersion };
            await Status(HttpMethod.Post, reservePath, null, 401, reservationInput);
            await Status(HttpMethod.Post, reservePath, owner, 400, reservationInput);
            await Status(HttpMethod.Get, $"/api/books/{rollbackBook.Id}/contact", reader1, 403);
            hook.ArmRace();
            var reservations = await Task.WhenAll(Send(HttpMethod.Post, reservePath, reader1, reservationInput), Send(HttpMethod.Post, reservePath, reader2, reservationInput));
            Require(reservations.Select(x => (int)x.StatusCode).Order().SequenceEqual(new[] { 200, 409 }));
            foreach (var response in reservations) response.Dispose();
            User winner;
            Guid reservedVersion;
            await using (var db = Open())
            {
                var reserved = await db.Books.SingleAsync(x => x.Id == rollbackBook.Id, ct);
                Require(reserved.Status == BookStatus.Reserved && reserved.ReservedById.HasValue && reserved.Version != reservationVersion);
                winner = reserved.ReservedById == reader1.Id ? reader1 : reader2;
                reservedVersion = reserved.Version;
                Require(!await db.Loans.AnyAsync(x => x.BookId == rollbackBook.Id, ct));
            }
            var loser = winner == reader1 ? reader2 : reader1;
            await Status(HttpMethod.Get, $"/api/books/{rollbackBook.Id}/contact", winner, 200);
            await Status(HttpMethod.Get, $"/api/books/{rollbackBook.Id}/contact", loser, 403);
            await Status(HttpMethod.Delete, reservePath, loser, 403, new ReservationRequest { Version = reservedVersion });
            await Status(HttpMethod.Post, "/api/loans", owner, 409, new { BookId = rollbackBook.Id, Version = reservedVersion, BorrowerEmail = loser.Email });
            await Status(HttpMethod.Delete, reservePath, winner, 409, reservationInput);
            await Status(HttpMethod.Delete, reservePath, winner, 200, new ReservationRequest { Version = reservedVersion });
            // Reset the test's comparison token after the explicitly checked release.
            await using (var db = Open()) rollbackBook.Version = (await db.Books.SingleAsync(x => x.Id == rollbackBook.Id, ct)).Version;

            Console.WriteLine("CHECK: coordinated owner/Admin delivery race before SQL save");
            hook.ArmRace();
            var calls = await Task.WhenAll(Send(HttpMethod.Post, "/api/loans", owner, Input(book.Id, reader1)),
                Send(HttpMethod.Post, "/api/loans", admin, Input(book.Id, reader2)));
            Require(calls.Select(x => (int)x.StatusCode).Order().SequenceEqual(new[] { 201, 409 }));
            foreach (var response in calls) response.Dispose();
            Guid loanId;
            Guid loanedVersion;
            await using (var db = Open())
            {
                var active = await db.Loans.Where(x => x.BookId == book.Id && x.ReturnedAtUtc == null).ToListAsync(ct);
                Require(active.Count == 1 && await db.Loans.CountAsync(ct) == 1);
                Require((await db.Books.SingleAsync(x => x.Id == book.Id, ct)).Status == BookStatus.Loaned);
                loanId = active.Single().Id;
                loanedVersion = (await db.Books.SingleAsync(x => x.Id == book.Id, ct)).Version;
            }

            Console.WriteLine("CHECK: rollback after actual SQL save of delivery and return");
            hook.FailAfterSave = true;
            await Status(HttpMethod.Post, "/api/loans", owner, 500, Input(rollbackBook.Id, reader1));
            await using (var db = Open())
            {
                Require(!await db.Loans.AnyAsync(x => x.BookId == rollbackBook.Id, ct));
                var unchanged = await db.Books.SingleAsync(x => x.Id == rollbackBook.Id, ct);
                Require(unchanged.Status == BookStatus.Available && unchanged.Version == rollbackBook.Version);
            }
            hook.FailAfterSave = true;
            await Status(HttpMethod.Put, $"/api/loans/{loanId}/return", owner, 500);
            await using (var db = Open())
            {
                Require((await db.Loans.SingleAsync(x => x.Id == loanId, ct)).ReturnedAtUtc is null);
                var unchanged = await db.Books.SingleAsync(x => x.Id == book.Id, ct);
                Require(unchanged.Status == BookStatus.Loaned && unchanged.Version == loanedVersion);
            }

            Console.WriteLine("CHECK: coordinated return concurrency and release");
            hook.ArmRace();
            var returns = await Task.WhenAll(Send(HttpMethod.Put, $"/api/loans/{loanId}/return", owner), Send(HttpMethod.Put, $"/api/loans/{loanId}/return", admin));
            Require(returns.Select(x => (int)x.StatusCode).Order().SequenceEqual(new[] { 200, 409 }));
            var conflict = returns.Single(x => (int)x.StatusCode == 409);
            Require((await conflict.Content.ReadFromJsonAsync<ApiError>(ct))!.Code == "version_conflict");
            foreach (var response in returns) response.Dispose();
            await using (var db = Open())
            {
                Require((await db.Loans.SingleAsync(x => x.Id == loanId, ct)).ReturnedAtUtc is not null);
                Require((await db.Books.SingleAsync(x => x.Id == book.Id, ct)).Status == BookStatus.Available);
            }
            await Status(HttpMethod.Put, $"/api/loans/{loanId}/return", owner, 409);
            await Status(HttpMethod.Delete, $"/api/books/{book.Id}", owner, 409);
            await using (var db = Open())
            {
                try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Books\" WHERE \"Id\" = {book.Id}", ct); throw new InvalidOperationException(); }
                catch (PostgresException e) when (e.SqlState is "23503" or "23001" && e.ConstraintName == "FK_Loans_Books_BookId") { }
            }
            await Status(HttpMethod.Get, $"/api/loans/{loanId}", reader1.Id == (await LoanBorrower()) ? reader2 : reader1, 403);
            async Task<Guid> LoanBorrower() { await using var db = Open(); return (await db.Loans.SingleAsync(x => x.Id == loanId, ct)).BorrowerId; }
            // A new delivery after return proves the partial index permits returned history.
            await Status(HttpMethod.Post, "/api/loans", owner, 201, Input(book.Id, reader1));
            using var loanPage = await Send(HttpMethod.Get, "/api/loans?side=owner&pageSize=1&pageNumber=2", owner);
            var lp = (await loanPage.Content.ReadFromJsonAsync<PagedResult<LoanResponse>>(ct))!;
            Require(lp.TotalCount == 2 && lp.Items.Count == 1);
            using var invisible = await Send(HttpMethod.Get, "/api/loans", reader2);
            var ip = (await invisible.Content.ReadFromJsonAsync<PagedResult<LoanResponse>>(ct))!;
            Require(ip.Items.All(x => x.BorrowerId == reader2.Id));
        }
        finally { await app.StopAsync(CancellationToken.None); }

        Console.WriteLine("CHECK: actual partial unique index, date checks and restrictive history foreign keys");
        async Task Constraint(string state, string name, Action<BookShareDbContext> change)
        {
            await using var db = Open(); change(db);
            try { await db.SaveChangesAsync(ct); throw new InvalidOperationException(); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException p && p.SqlState == state && p.ConstraintName == name) { }
        }
        Loan NewLoan(Guid id) => new() { BookId = id, BorrowerId = reader2.Id, RecordedByUserId = owner.Id, BorrowedAtUtc = DateTime.UtcNow, DueAtUtc = DateTime.UtcNow.AddDays(14) };
        await Constraint("23505", "IX_Loans_ActiveBook", db => db.Add(NewLoan(book.Id)));
        await Constraint("23514", "CK_Loans_DueAtUtc", db => { var l = NewLoan(rollbackBook.Id); l.DueAtUtc = l.BorrowedAtUtc; db.Add(l); });
        await Constraint("23514", "CK_Loans_ReturnedAtUtc", db => { var l = NewLoan(rollbackBook.Id); l.ReturnedAtUtc = l.BorrowedAtUtc.AddTicks(-10); db.Add(l); });
        // Raw SQL bypasses service checks and EF relationship fixup, proving the database FK.
        await using (var db = Open())
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Books\" WHERE \"Id\" = {book.Id}", ct); throw new InvalidOperationException(); }
            catch (PostgresException e) when (e.SqlState is "23503" or "23001" && e.ConstraintName == "FK_Loans_Books_BookId") { }
        }
        Console.WriteLine("CHECK: real history FK error is translated specifically by UnitOfWork");
        await using (var db = Open())
        {
            db.Books.Remove(await db.Books.SingleAsync(x => x.Id == book.Id, ct));
            await using var unit = new UnitOfWork(db);
            try { await unit.SaveChangesAsync(ct); throw new InvalidOperationException(); }
            catch (BookShare.Application.Services.AppException e) when (e.Status == 409 && e.Code == "loan_history") { }
        }
        Console.WriteLine("CHECK: borrower/recorder history foreign keys and assigned-category RESTRICT translation");
        var history = NewLoan(rollbackBook.Id);
        history.ReturnedAtUtc = history.BorrowedAtUtc;
        history.RecordedByUserId = admin.Id;
        var category = new Category { Name = "History", NormalizedName = "HISTORY" };
        await using (var db = Open())
        {
            db.Add(history); db.Add(category);
            db.Add(new BookCategory { BookId = rollbackBook.Id, CategoryId = category.Id });
            await db.SaveChangesAsync(ct);
        }
        foreach (var target in new[] { (reader2.Id, "FK_Loans_Users_BorrowerId"), (admin.Id, "FK_Loans_Users_RecordedByUserId") })
        {
            await using var db = Open();
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {target.Id}", ct); throw new InvalidOperationException(); }
            catch (PostgresException e) when (e.SqlState is "23503" or "23001" && e.ConstraintName == target.Item2) { }
        }
        await using (var db = Open())
        {
            db.Categories.Remove(await db.Categories.SingleAsync(x => x.Id == category.Id, ct));
            await using var unit = new UnitOfWork(db);
            try { await unit.SaveChangesAsync(ct); throw new InvalidOperationException(); }
            catch (BookShare.Application.Services.AppException e) when (e.Status == 409 && e.Code == "category_assigned") { }
        }
        Console.WriteLine("CHECK: unrelated actual check-constraint errors are not converted to conflict");
        await using (var db = Open())
        {
            var invalid = NewLoan(rollbackBook.Id); invalid.DueAtUtc = invalid.BorrowedAtUtc;
            db.Add(invalid);
            await using var unit = new UnitOfWork(db);
            try { await unit.SaveChangesAsync(ct); throw new InvalidOperationException(); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23514", ConstraintName: "CK_Loans_DueAtUtc" }) { }
        }
        Console.WriteLine("PASSED PostgreSQL: token, coordinated delivery/return races, SQL rollback, constraints, JWT authorization and pagination. Test data retained in the isolated database.");
    }
    private static User User(string name) => new() { FullName = name, Email = name + "@example.com", NormalizedEmail = name.ToUpperInvariant() + "@EXAMPLE.COM", PasswordHash = "test-only", ContactPhone = "0500000000" };
    private static Book Book(User owner) => new() { OwnerId = owner.Id, Title = "Test", Author = "Author", Description = "Description", City = "Haifa" };
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }

    private sealed class SaveHook : SaveChangesInterceptor
    {
        private int arrivals;
        private TaskCompletionSource? gate;
        private Guid? firstVersion;
        public bool FailAfterSave { get; set; }
        public void ArmRace() { arrivals = 0; firstVersion = null; gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var current = gate;
            if (current is not null)
            {
                var version = eventData.Context!.ChangeTracker.Entries<Book>().Single().Property(x => x.Version).OriginalValue;
                lock (this)
                {
                    if (firstVersion is null) firstVersion = version;
                    else Require(firstVersion == version);
                }
                if (Interlocked.Increment(ref arrivals) == 2) { gate = null; current.TrySetResult(); }
                await current.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (FailAfterSave)
            {
                Require(result >= 2 && eventData.Context!.Database.CurrentTransaction is not null);
                FailAfterSave = false;
                throw new InvalidOperationException("Test failure after SQL, before commit.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
