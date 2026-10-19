using BookShare.Domain.Enums;

namespace BookShare.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Loan> RecordedLoans { get; set; } = new List<Loan>();
    public ICollection<Book> Books { get; set; } = new List<Book>();
}
