using System.ComponentModel.DataAnnotations;
using BookShare.Domain.Enums;

namespace BookShare.Application.DTOs;

public sealed class RegisterUserRequest
{
    [Required, StringLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
    [Required, Phone, StringLength(30)] public string ContactPhone { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Password { get; set; } = string.Empty;
}

public sealed record UserResponse(Guid Id, string FullName, string Email, string ContactPhone, UserRole Role);
public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
public sealed record CategoryResponse(Guid Id, string Name);

public sealed class CategoryRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
}

public class CreateBookRequest
{
    [Required, StringLength(300)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Author { get; set; } = string.Empty;
    [Required, StringLength(4000)] public string Description { get; set; } = string.Empty;
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [Required] public List<Guid> CategoryIds { get; set; } = [];
}

public sealed record BookResponse(Guid Id, Guid OwnerId, string Title, string Author,
    string Description, string City, BookStatus Status, Guid Version, DateTime CreatedAtUtc,
    IReadOnlyList<CategoryResponse> Categories);

public sealed record BookContactResponse(string FullName, string Email, string ContactPhone);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize);

public sealed class UpdateBookRequest : CreateBookRequest
{
    public Guid Version { get; set; }
    [EnumDataType(typeof(BookStatus))] public BookStatus Status { get; set; }
}

public sealed class BookQuery
{
    [StringLength(300)] public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    [StringLength(100)] public string? City { get; set; }
    public bool AvailableOnly { get; set; }
    public bool Mine { get; set; }
    [RegularExpression("^(title|title_desc|date|date_desc)$")] public string SortBy { get; set; } = "date_desc";
    [Range(1, int.MaxValue)] public int PageNumber { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 10;
}

public sealed record Actor(Guid UserId, bool IsAdmin);

public sealed class CreateLoanRequest
{
    public Guid BookId { get; set; }
    [Required, EmailAddress, StringLength(254)] public string BorrowerEmail { get; set; } = string.Empty;
}
public sealed record LoanResponse(Guid Id, Guid BookId, Guid BorrowerId, Guid RecordedByUserId,
    DateTime BorrowedAtUtc, DateTime DueAtUtc, DateTime? ReturnedAtUtc);
public sealed class LoanQuery
{
    [RegularExpression("^(all|owner|borrower)$")] public string Side { get; set; } = "all";
    [RegularExpression("^(all|active|returned|overdue)$")] public string Status { get; set; } = "all";
    [Range(1, int.MaxValue)] public int PageNumber { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 10;
}
public sealed class LoanOptions
{
    public const string SectionName = "Loans";
    [Range(1, 365)] public int DurationDays { get; set; } = 14;
}
