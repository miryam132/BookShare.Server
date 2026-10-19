using BookShare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Infrastructure.Persistence;

public sealed class BookShareDbContext(DbContextOptions<BookShareDbContext> options)
    : DbContext(options)
{
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BookCategory> BookCategories => Set<BookCategory>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefreshBookVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshBookVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    private void RefreshBookVersions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<Book>().Where(x => x.State is EntityState.Added or EntityState.Modified))
            entry.Entity.Version = Guid.NewGuid();
        foreach (var entry in ChangeTracker.Entries<BookCategory>().Where(x => x.State is EntityState.Added or EntityState.Deleted))
        {
            var book = ChangeTracker.Entries<Book>().SingleOrDefault(x => x.Entity.Id == entry.Entity.BookId);
            if (book is not null && book.State != EntityState.Deleted) book.Property(x => x.Version).CurrentValue = Guid.NewGuid();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookShareDbContext).Assembly);
}
