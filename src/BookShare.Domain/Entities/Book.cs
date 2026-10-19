using BookShare.Domain.Enums;

namespace BookShare.Domain.Entities;

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid? ReservedById { get; set; }
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public BookStatus Status { get; set; } = BookStatus.Available;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<BookCategory> BookCategories { get; set; } = new List<BookCategory>();
}
