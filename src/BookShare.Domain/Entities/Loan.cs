namespace BookShare.Domain.Entities;
public sealed class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public Book Book { get; set; } = null!;
    public Guid BorrowerId { get; set; }
    public User Borrower { get; set; } = null!;
    public Guid RecordedByUserId { get; set; }
    public User RecordedByUser { get; set; } = null!;
    public DateTime BorrowedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
}
