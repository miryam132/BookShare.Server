using BookShare.Application.Services;
using BookShare.Domain.Entities;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

public static class LoanInfrastructureChecks
{
    public static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        DbContextOptions<BookShareDbContext> Options(SaveChangesInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<BookShareDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused");
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return builder.Options;
        }
        await using (var context = new BookShareDbContext(Options()))
        {
            var model = context.GetServiceModel();
            var book = model.FindEntityType(typeof(Book))!;
            Check(book.FindProperty(nameof(Book.Version))!.IsConcurrencyToken, "Book Version must be a concurrency token.");
            var loan = model.FindEntityType(typeof(Loan))!;
            var activeIndex = loan.GetIndexes().Single(x => x.GetDatabaseName() == "IX_Loans_ActiveBook");
            Check(activeIndex.IsUnique && activeIndex.GetFilter() == "\"ReturnedAtUtc\" IS NULL", "Active loan index is missing/incorrect.");
            Check(loan.GetCheckConstraints().Count() == 2, "Loan date constraints missing.");
            Check(loan.GetForeignKeys().Count() == 3 && loan.GetForeignKeys().All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "Loan history foreign keys must restrict deletion.");
        }
        var concurrency = new DbUpdateConcurrencyException("synthetic");
        var active = new DbUpdateException("synthetic", new PostgresException("synthetic", "ERROR", "ERROR", "23505", constraintName: "IX_Loans_ActiveBook"));
        var unrelated = new DbUpdateException("synthetic", new PostgresException("synthetic", "ERROR", "ERROR", "23505", constraintName: "OtherIndex"));
        foreach (var failure in new Exception[] { concurrency, active, unrelated })
        {
            await using var context = new BookShareDbContext(Options(new FailureInterceptor(failure)));
            context.Books.Add(new Book());
            await using var unit = new UnitOfWork(context);
            try { await unit.SaveChangesAsync(default); throw new Exception("Expected failure was not thrown."); }
            catch (AppException ex) when (!ReferenceEquals(failure, unrelated))
            {
                Check(ex.Status == 409, "Infrastructure did not translate conflict to 409.");
                Check(!context.ChangeTracker.Entries().Any(), "Conflict left tracked changes.");
            }
            catch (DbUpdateException ex) when (ReferenceEquals(failure, unrelated))
            { Check(ReferenceEquals(ex, unrelated), "Unrelated database error was misclassified."); }
        }
        await using (var context = new BookShareDbContext(Options(new SuppressSaveInterceptor())))
        {
            var book = new Book(); var first = book.Version;
            context.Books.Add(book);
            await context.SaveChangesAsync(default);
            Check(book.Version != first && book.Version != Guid.Empty, "Async save did not generate Version.");
            context.ChangeTracker.AcceptAllChanges(); first = book.Version; book.Title = "Changed";
            await context.SaveChangesAsync(true, default);
            Check(book.Version != first && context.Entry(book).Property(x => x.Version).OriginalValue == first, "Async save lost original Version.");
            context.ChangeTracker.AcceptAllChanges(); first = book.Version; book.Author = "Changed";
            context.SaveChanges();
            Check(book.Version != first, "Sync save did not refresh Version.");
            context.ChangeTracker.AcceptAllChanges(); first = book.Version;
            context.BookCategories.Add(new BookCategory { BookId = book.Id, CategoryId = Guid.NewGuid() });
            await context.SaveChangesAsync(default);
            Check(book.Version != first, "Category-only edit did not refresh book Version.");
        }
        Console.WriteLine($"Passed {checks} offline EF model/version/error-translation checks; synthetic failures, no SQL execution or real rollback.");
    }
    private static IModel GetServiceModel(this BookShareDbContext context)
        => Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;
    private sealed class FailureInterceptor(Exception failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
            => throw failure;
    }
    private sealed class SuppressSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
            => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => InterceptionResult<int>.SuppressWithResult(1);
    }
}
