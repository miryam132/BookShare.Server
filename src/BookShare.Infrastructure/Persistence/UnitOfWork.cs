using BookShare.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookShare.Infrastructure.Persistence;

public sealed class UnitOfWork(BookShareDbContext context) : IUnitOfWork, IDisposable, IAsyncDisposable
{
    private IDbContextTransaction? transaction;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { return await context.SaveChangesAsync(cancellationToken); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException { SqlState: "23503" or "23001", ConstraintName: "FK_Loans_Books_BookId" }
                && context.ChangeTracker.Entries<BookShare.Domain.Entities.Book>().Any(x => x.State == Microsoft.EntityFrameworkCore.EntityState.Deleted))
        {
            if (transaction is not null) await RollbackTransactionAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw new BookShare.Application.Services.AppException(409, "loan_history", "A book with loan history cannot be deleted.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            if (transaction is not null) await RollbackTransactionAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw new BookShare.Application.Services.AppException(409, "version_conflict", "Book has changed; reload before retrying.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "IX_Loans_ActiveBook" })
        {
            if (transaction is not null) await RollbackTransactionAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw new BookShare.Application.Services.AppException(409, "active_loan_conflict", "Book already has an active loan.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException
                { SqlState: "23505", ConstraintName: "IX_Users_NormalizedEmail" })
        {
            context.ChangeTracker.Clear();
            throw new BookShare.Application.Services.AppException(409, "email_conflict", "Email is already registered.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException
                { SqlState: "23505", ConstraintName: "IX_Categories_NormalizedName" })
        {
            context.ChangeTracker.Clear();
            throw new BookShare.Application.Services.AppException(409, "category_conflict", "Category already exists.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException
                { SqlState: "23503" or "23001", ConstraintName: "FK_BookCategories_Categories_CategoryId" })
        {
            var deleting = context.ChangeTracker.Entries<BookShare.Domain.Entities.Category>()
                .Any(x => x.State == Microsoft.EntityFrameworkCore.EntityState.Deleted);
            context.ChangeTracker.Clear();
            throw deleting
                ? new BookShare.Application.Services.AppException(409, "category_assigned", "Remove category assignments first.")
                : new BookShare.Application.Services.AppException(400, "invalid_categories", "A category no longer exists.");
        }

    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null) throw new InvalidOperationException("A transaction is already active.");
        transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        var current = transaction ?? throw new InvalidOperationException("No active transaction.");
        await current.CommitAsync(cancellationToken);
        await current.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        var current = transaction;
        if (current is null) { context.ChangeTracker.Clear(); return; }
        await current.RollbackAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await current.DisposeAsync();
        transaction = null;
    }

    public void Dispose()
    {
        transaction?.Dispose();
        transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        transaction = null;
    }
}
