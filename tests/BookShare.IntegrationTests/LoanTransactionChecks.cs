using BookShare.Application;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Application.Services;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

public static class LoanTransactionChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var services = new ServiceCollection();
        services.AddLogging(); services.AddApplication();
        services.Configure<LoanOptions>(x => x.DurationDays = 21);
        services.AddSingleton<IUserRepository, MemoryUsers>();
        services.AddSingleton<ICategoryRepository, MemoryCategories>();
        services.AddSingleton<IBookRepository, TransactionBooks>();
        services.AddSingleton<ILoanRepository, MemoryLoans>();
        services.AddSingleton<TransactionUnit>();
        services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<TransactionUnit>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var unit = provider.GetRequiredService<TransactionUnit>();
        var users = provider.GetRequiredService<IUserRepository>();
        var owner = new User { NormalizedEmail = "OWNER@EXAMPLE.COM" };
        var borrower = new User { NormalizedEmail = "BORROWER@EXAMPLE.COM" };
        users.Add(owner); users.Add(borrower);
        var books = provider.GetRequiredService<IBookRepository>();
        var book = new Book { OwnerId = owner.Id }; books.Add(book);
        var service = scope.ServiceProvider.GetRequiredService<ILoanService>();
        var actor = new Actor(owner.Id, false);
        var request = new CreateLoanRequest { BookId = book.Id, BorrowerEmail = "borrower@example.com" };
        var loan = await service.CreateAsync(request, actor, default);
        Check(unit.Events.SequenceEqual(new[] { "begin", "tracked-read", "save", "commit" }), "Delivery transaction order incorrect.");
        Check(loan.DueAtUtc - loan.BorrowedAtUtc == TimeSpan.FromDays(21), "Configured lending duration ignored.");
        unit.Events.Clear();
        await service.ReturnAsync(loan.Id, actor, default);
        Check(unit.Events.SequenceEqual(new[] { "begin", "save", "commit" }), "Return transaction order incorrect.");
        unit.Events.Clear();
        try { await service.ReturnAsync(loan.Id, actor, default); throw new Exception("Double return accepted."); }
        catch (AppException ex) { Check(ex.Status == 409 && unit.Events.SequenceEqual(new[] { "begin", "rollback" }), "Rejected return must roll back."); }
        var failingBook = new Book { OwnerId = owner.Id }; books.Add(failingBook); request.BookId = failingBook.Id;
        unit.Events.Clear(); unit.FailSave = true;
        try { await service.CreateAsync(request, actor, default); throw new Exception("Save failure ignored."); }
        catch (AppException ex) { Check(ex.Status == 409 && unit.Events.SequenceEqual(new[] { "begin", "tracked-read", "save", "rollback" }), "Failed delivery must roll back without commit/retry."); }
        Console.WriteLine($"Passed {checks} transaction orchestration/options checks with doubles; rollback calls only, no database atomicity proof.");
    }
    private sealed class TransactionUnit : IUnitOfWork
    {
        public List<string> Events { get; } = [];
        public bool Active { get; private set; }
        public bool FailSave { get; set; }
        public Task BeginTransactionAsync(CancellationToken ct) { Active = true; Events.Add("begin"); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        { Events.Add("save"); if (FailSave) throw new AppException(409, "synthetic", "Simulated persistence conflict."); return Task.FromResult(1); }
        public Task CommitTransactionAsync(CancellationToken ct) { Active = false; Events.Add("commit"); return Task.CompletedTask; }
        public Task RollbackTransactionAsync(CancellationToken ct) { Active = false; Events.Add("rollback"); return Task.CompletedTask; }
    }
    private sealed class TransactionBooks(TransactionUnit unit) : IBookRepository
    {
        private readonly MemoryBooks inner = new();
        public void Add(Book book) => inner.Add(book);
        public void Remove(Book book) => inner.Remove(book);
        public Task<PagedResult<Book>> ListAsync(BookQuery query, Guid? ownerId, CancellationToken ct) => inner.ListAsync(query, ownerId, ct);
        public Task<Book?> FindByIdAsync(Guid id, bool tracking, CancellationToken ct)
        {
            if (!tracking || !unit.Active) throw new Exception("Book read must track inside active transaction.");
            unit.Events.Add("tracked-read"); return inner.FindByIdAsync(id, tracking, ct);
        }
    }
}
