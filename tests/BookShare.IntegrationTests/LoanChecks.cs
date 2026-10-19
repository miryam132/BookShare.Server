using System.Net.Http.Json;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

public static class LoanChecks
{
    public static async Task RunAsync(HttpClient client, IServiceProvider services, User owner)
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        async Task Status(HttpResponseMessage response, int expected)
        { using (response) Check((int)response.StatusCode == expected, $"Loan HTTP expected {expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); }
        void As(User user)
        {
            using var scope = services.CreateScope();
            client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<ITokenIssuer>().Issue(user).AccessToken);
        }
        var original = client.DefaultRequestHeaders.Authorization;
        var users = services.GetRequiredService<IUserRepository>();
        var borrower = new User { Email = "borrower@example.com", NormalizedEmail = "BORROWER@EXAMPLE.COM" };
        var outsider = new User { Email = "outsider@example.com" };
        var admin = new User { Role = UserRole.Admin };
        users.Add(borrower); users.Add(outsider); users.Add(admin);
        As(owner);
        var input = new CreateBookRequest { Title = "Loan book", Author = "Author", Description = "Description", City = "Haifa" };
        using var created = await client.PostAsJsonAsync("/api/books", input);
        var book = (await created.Content.ReadFromJsonAsync<BookResponse>())!;
        var update = new UpdateBookRequest { Title = input.Title, Author = input.Author, Description = input.Description, City = input.City, Version = book.Version };
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 200);
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 409);
        var request = new CreateLoanRequest { BookId = book.Id, BorrowerEmail = borrower.Email };
        client.DefaultRequestHeaders.Authorization = null;
        await Status(await client.PostAsJsonAsync("/api/loans", request), 401);
        await Status(await client.GetAsync("/api/loans"), 401);
        As(outsider);
        await Status(await client.PostAsJsonAsync("/api/loans", request), 403);
        As(owner);
        await Status(await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest { BookId = book.Id, BorrowerEmail = "missing@example.com" }), 400);
        await Status(await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest { BookId = book.Id, BorrowerEmail = owner.Email }), 400);
        await Status(await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest { BookId = Guid.NewGuid(), BorrowerEmail = borrower.Email }), 404);
        var before = DateTime.UtcNow;
        using var delivery = await client.PostAsJsonAsync("/api/loans", request);
        Check((int)delivery.StatusCode == 201 && delivery.Headers.Location is not null, "Loan creation/Location failed.");
        var loan = (await delivery.Content.ReadFromJsonAsync<LoanResponse>())!;
        Check(loan.RecordedByUserId == owner.Id && loan.BorrowerId == borrower.Id && loan.BorrowedAtUtc >= before && loan.BorrowedAtUtc.Kind == DateTimeKind.Utc && loan.DueAtUtc - loan.BorrowedAtUtc == TimeSpan.FromDays(14), "Loan identity or dates failed.");
        var loaned = (await client.GetFromJsonAsync<BookResponse>($"/api/books/{book.Id}"))!;
        Check(loaned.Status == BookStatus.Loaned && loaned.Version != book.Version, "Loan did not update book.");
        await Status(await client.PostAsJsonAsync("/api/loans", request), 409);
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 409);
        await Status(await client.DeleteAsync($"/api/books/{book.Id}"), 409);
        As(outsider);
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 403);
        await Status(await client.GetAsync($"/api/loans/{loan.Id}"), 403);
        await Status(await client.PutAsync($"/api/loans/{loan.Id}/return", null), 403);
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans"))!.TotalCount == 0, "Outsider saw loan list.");
        As(borrower);
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 200);
        await Status(await client.GetAsync($"/api/loans/{loan.Id}"), 200);
        await Status(await client.PutAsync($"/api/loans/{loan.Id}/return", null), 403);
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans?side=borrower&status=active&pageSize=1"))!.Items.Single().Id == loan.Id, "Borrower filter failed.");
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans?side=owner"))!.TotalCount == 0, "Owner side failed.");
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans?pageNumber=2&pageSize=1"))!.Items.Count == 0, "Loan pagination failed.");
        await Status(await client.GetAsync("/api/loans?pageSize=51"), 400);
        await Status(await client.GetAsync("/api/loans?side=invalid"), 400);
        await Status(await client.GetAsync("/api/loans?status=invalid"), 400);
        As(admin);
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans"))!.TotalCount == 1, "Admin list failed.");
        await Status(await client.GetAsync($"/api/loans/{loan.Id}"), 200);
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 200);
        using var returned = await client.PutAsync($"/api/loans/{loan.Id}/return", null);
        Check((int)returned.StatusCode == 200 && (await returned.Content.ReadFromJsonAsync<LoanResponse>())!.ReturnedAtUtc >= loan.BorrowedAtUtc, "Return failed.");
        await Status(await client.PutAsync($"/api/loans/{loan.Id}/return", null), 409);
        As(owner);
        var available = (await client.GetFromJsonAsync<BookResponse>($"/api/books/{book.Id}"))!;
        Check(available.Status == BookStatus.Available && available.Version != loaned.Version, "Return did not release book.");
        await Status(await client.DeleteAsync($"/api/books/{book.Id}"), 409);
        update.Version = available.Version; update.Status = BookStatus.Hidden;
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 200);
        await Status(await client.PostAsJsonAsync("/api/loans", request), 409);
        As(borrower);
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 404);
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans?status=returned"))!.TotalCount == 1, "Returned filter failed.");
        Check((await client.GetFromJsonAsync<PagedResult<LoanResponse>>("/api/loans?status=active"))!.TotalCount == 0, "Active filter failed.");
        client.DefaultRequestHeaders.Authorization = original;
        Console.WriteLine($"Passed {checks} loan HTTP checks; memory repositories, no PostgreSQL concurrency/rollback proof.");
    }
}
public sealed class MemoryLoans : ILoanRepository
{
    public List<Loan> Items { get; } = [];
    public void Add(Loan loan) => Items.Add(loan);
    public Task<Loan?> FindByIdAsync(Guid id, bool tracking, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
    public Task<bool> HasHistoryAsync(Guid bookId, CancellationToken ct) => Task.FromResult(Items.Any(x => x.BookId == bookId));
    public Task<bool> IsActiveBorrowerAsync(Guid bookId, Guid userId, CancellationToken ct) => Task.FromResult(Items.Any(x => x.BookId == bookId && x.BorrowerId == userId && x.ReturnedAtUtc == null));
    public Task<PagedResult<Loan>> ListAsync(LoanQuery query, Actor actor, CancellationToken ct)
    {
        IEnumerable<Loan> rows = Items.Where(x => actor.IsAdmin || x.Book.OwnerId == actor.UserId || x.BorrowerId == actor.UserId);
        if (query.Side == "owner") rows = rows.Where(x => x.Book.OwnerId == actor.UserId);
        if (query.Side == "borrower") rows = rows.Where(x => x.BorrowerId == actor.UserId);
        if (query.Status == "active") rows = rows.Where(x => x.ReturnedAtUtc == null);
        if (query.Status == "returned") rows = rows.Where(x => x.ReturnedAtUtc != null);
        if (query.Status == "overdue") rows = rows.Where(x => x.ReturnedAtUtc == null && x.DueAtUtc < DateTime.UtcNow);
        var offset = ((long)query.PageNumber - 1) * query.PageSize;
        var items = offset > int.MaxValue ? new List<Loan>() : rows.OrderByDescending(x => x.BorrowedAtUtc).ThenBy(x => x.Id).Skip((int)offset).Take(query.PageSize).ToList();
        return Task.FromResult(new PagedResult<Loan>(items, rows.Count(), query.PageNumber, query.PageSize));
    }
}
