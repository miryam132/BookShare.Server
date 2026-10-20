using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Application.Services;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BookShare.Application.Tests;

public sealed class ServiceTests
{
    private readonly Mock<IBookRepository> books = new();
    private readonly Mock<ILoanRepository> loans = new();
    private readonly Mock<IUserRepository> users = new();
    private readonly Mock<ICategoryRepository> categories = new();
    private readonly Mock<IUnitOfWork> unit = new();
    private readonly Mock<IMapper> mapper = new();
    private readonly Book book = new() { OwnerId = Guid.NewGuid() };
    private readonly User borrower = new() { Email = "reader@example.com" };
    private readonly CancellationToken ct = new CancellationTokenSource().Token;
    private Actor Owner => new(book.OwnerId, false);
    private LoanService Service => new(loans.Object, books.Object, users.Object, unit.Object, mapper.Object, Options.Create(new LoanOptions()));
    private CreateLoanRequest Request => new() { BookId = book.Id, BorrowerEmail = " reader@example.com " };

    public ServiceTests()
    {
        books.Setup(x => x.FindByIdAsync(book.Id, true, ct)).ReturnsAsync(book);
        users.Setup(x => x.FindByNormalizedEmailAsync("READER@EXAMPLE.COM", ct, false)).ReturnsAsync(borrower);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delivery_records_identity_dates_and_saves(bool admin)
    {
        Loan? recorded = null;
        loans.Setup(x => x.Add(It.IsAny<Loan>())).Callback<Loan>(x => recorded = x);
        var actor = admin ? new Actor(Guid.NewGuid(), true) : Owner;
        var before = DateTime.UtcNow;
        await Service.CreateAsync(Request, actor, ct);
        Assert.NotNull(recorded);
        Assert.Equal(book.Id, recorded.BookId);
        Assert.Equal(borrower.Id, recorded.BorrowerId);
        Assert.Equal(actor.UserId, recorded.RecordedByUserId);
        Assert.InRange(recorded.BorrowedAtUtc, before, DateTime.UtcNow);
        Assert.Equal(TimeSpan.FromDays(14), recorded.DueAtUtc - recorded.BorrowedAtUtc);
        Assert.Equal(BookStatus.Loaned, book.Status);
        unit.Verify(x => x.SaveChangesAsync(ct), Times.Once);
        unit.Verify(x => x.CommitTransactionAsync(ct), Times.Once);
    }

    [Theory]
    [InlineData("busy", 409, "book_unavailable")]
    [InlineData("self", 400, "self_loan")]
    [InlineData("outsider", 403, "forbidden")]
    [InlineData("missing", 400, "borrower_not_found")]
    public async Task Invalid_delivery_does_not_save(string scenario, int status, string code)
    {
        if (scenario == "busy") book.Status = BookStatus.Loaned;
        if (scenario == "self") borrower.Id = book.OwnerId;
        if (scenario == "missing") users.Setup(x => x.FindByNormalizedEmailAsync("READER@EXAMPLE.COM", ct, false)).ReturnsAsync((User?)null);
        var actor = scenario == "outsider" ? new Actor(Guid.NewGuid(), false) : Owner;
        var error = await Assert.ThrowsAsync<AppException>(() => Service.CreateAsync(Request, actor, ct));
        Assert.Equal(status, error.Status); Assert.Equal(code, error.Code);
        loans.Verify(x => x.Add(It.IsAny<Loan>()), Times.Never);
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        unit.Verify(x => x.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Return_and_duplicate_return(bool alreadyReturned)
    {
        book.Status = BookStatus.Loaned;
        var loan = new Loan { Book = book, ReturnedAtUtc = alreadyReturned ? DateTime.UtcNow : null };
        loans.Setup(x => x.FindByIdAsync(loan.Id, true, ct)).ReturnsAsync(loan);
        if (alreadyReturned)
        {
            Assert.Equal("already_returned", (await Assert.ThrowsAsync<AppException>(() => Service.ReturnAsync(loan.Id, Owner, ct))).Code);
            unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            await Service.ReturnAsync(loan.Id, Owner, ct);
            Assert.NotNull(loan.ReturnedAtUtc); Assert.Equal(BookStatus.Available, book.Status);
            unit.Verify(x => x.SaveChangesAsync(ct), Times.Once);
            unit.Verify(x => x.CommitTransactionAsync(ct), Times.Once);
        }
    }

    [Fact]
    public async Task Cannot_edit_someone_elses_book()
    {
        var service = new BookService(books.Object, categories.Object, users.Object, loans.Object, unit.Object, mapper.Object);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => service.UpdateAsync(book.Id, new UpdateBookRequest(), new Actor(Guid.NewGuid(), false), ct))).Status);
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cannot_delete_book_with_history()
    {
        loans.Setup(x => x.HasHistoryAsync(book.Id, ct)).ReturnsAsync(true);
        var service = new BookService(books.Object, categories.Object, users.Object, loans.Object, unit.Object, mapper.Object);
        Assert.Equal("loan_history", (await Assert.ThrowsAsync<AppException>(() => service.DeleteAsync(book.Id, Owner, ct))).Code);
        books.Verify(x => x.Remove(It.IsAny<Book>()), Times.Never);
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
