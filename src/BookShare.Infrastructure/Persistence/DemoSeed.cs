using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using BookShare.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BookShare.Infrastructure.Persistence;

/// <summary>Explicit, additive demo provisioning. Never invoked by normal API startup.</summary>
public static class DemoSeed
{
    public static Guid Id(int number) => Guid.Parse($"b0090000-0000-4000-8000-{number:000000000000}");
    public static async Task RunAsync(BookShareDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>("DemoSeed:Enabled"))
            throw new InvalidOperationException("Demo seed must be explicitly enabled.");
        var roles = new[] { "Admin", "Owner", "Borrower1", "Borrower2" };
        // Validate every secret before touching data. No defaults or secret values in errors.
        var passwords = roles.Select(role => configuration[$"DemoSeed:{role}Password"]).ToArray();
        if (passwords.Any(p => string.IsNullOrWhiteSpace(p) || p.Length < 16 || p.Length > 128) || passwords.Distinct().Count() != 4)
            throw new InvalidOperationException("Supply four distinct demo passwords, 16-128 characters each, through secrets.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Apply reviewed migrations before seeding.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize concurrent seed processes. Regular application operations are never retried.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(9009009)", ct);
        var security = new PasswordSecurity();
        for (var i = 0; i < roles.Length; i++)
        {
            var id = Id(i + 1);
            var email = $"{roles[i].ToLowerInvariant()}@demo.bookshare.example";
            var role = i == 0 ? UserRole.Admin : UserRole.Member;
            var existing = await db.Users.SingleOrDefaultAsync(u => u.Id == id || u.NormalizedEmail == email.ToUpperInvariant(), ct);
            if (existing is not null)
            {
                if (existing.Id != id || existing.NormalizedEmail != email.ToUpperInvariant() || existing.Role != role)
                    throw new InvalidOperationException("Demo identity collision; no accounts were changed.");
                continue; // Never elevate an existing member, overwrite a hash or reset credentials.
            }
            var user = new User { Id = id, FullName = $"Demo {roles[i]}", Email = email,
                NormalizedEmail = email.ToUpperInvariant(), ContactPhone = "0500000000", Role = role };
            user.PasswordHash = security.Hash(user, passwords[i]!);
            db.Users.Add(user);
        }
        var names = new[] { "ספרות", "מדע", "ילדים", "היסטוריה" };
        var categories = new List<Category>();
        for (var i = 0; i < names.Length; i++)
        {
            var normalized = names[i].ToUpperInvariant();
            var category = await db.Categories.SingleOrDefaultAsync(c => c.NormalizedName == normalized, ct);
            if (category is null)
            {
                category = new Category { Name = names[i], NormalizedName = normalized };
                db.Categories.Add(category);
            }
            categories.Add(category);
        }
        for (var i = 0; i < 12; i++)
        {
            var id = Id(100 + i);
            if (await db.Books.AnyAsync(b => b.Id == id, ct)) continue;
            var book = new Book { Id = id, OwnerId = Id(2),
                Title = i == 0 ? "ספר הדגמת תחרות" : $"ספר דמו {i + 1:00}", Author = "מחבר לדוגמה",
                Description = "מודעת הדגמה של עותק פיזי אחד.", City = "ירושלים" };
            book.BookCategories.Add(new BookCategory { BookId = id, CategoryId = categories[i % 4].Id });
            if (i % 2 == 0) book.BookCategories.Add(new BookCategory { BookId = id, CategoryId = categories[(i + 1) % 4].Id });
            db.Books.Add(book);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
