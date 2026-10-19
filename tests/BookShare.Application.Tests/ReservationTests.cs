using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Application.Services;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Moq;
using Xunit;

namespace BookShare.Application.Tests;

public sealed class ReservationTests
{
    private readonly Book book = new() { OwnerId = Guid.NewGuid() };
    private readonly User reader = new();
    private readonly Mock<IBookRepository> books = new();
    private readonly Mock<IUserRepository> users = new();
    private readonly Mock<IUnitOfWork> unit = new();
    private readonly Mock<IMapper> mapper = new();
    private BookService Service => new(books.Object, Mock.Of<ICategoryRepository>(), users.Object,
        Mock.Of<ILoanRepository>(), unit.Object, mapper.Object);
    public ReservationTests()
    {
        books.Setup(x => x.FindByIdAsync(book.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        users.Setup(x => x.FindByIdAsync(reader.Id, It.IsAny<CancellationToken>())).ReturnsAsync(reader);
        mapper.Setup(x => x.Map<BookResponse>(book)).Returns(() => new(book.Id, book.OwnerId,
            book.Title, book.Author, book.Description, book.City, book.Status, book.Version, book.CreatedAtUtc, []));
    }
    [Fact]
    public async Task Reserve_and_cancel_release_without_creating_a_loan()
    {
        var actor = new Actor(reader.Id, false);
        var result = await Service.ReserveAsync(book.Id, new() { Version = book.Version }, actor, default);
        Assert.True(result.IsReservedByMe);
        Assert.Equal(reader.Id, book.ReservedById);
        Assert.Equal(BookStatus.Reserved, book.Status);
        await Service.CancelReservationAsync(book.Id, new() { Version = book.Version }, actor, default);
        Assert.Equal(BookStatus.Available, book.Status);
        Assert.Null(book.ReservedById);
        unit.Verify(x => x.CommitTransactionAsync(default), Times.Exactly(2));
    }
    [Theory]
    [InlineData("self", 400)]
    [InlineData("stale", 409)]
    [InlineData("occupied", 409)]
    public async Task Invalid_reservation_never_saves(string scenario, int status)
    {
        var actor = new Actor(scenario == "self" ? book.OwnerId : reader.Id, false);
        if (scenario == "occupied") book.Status = BookStatus.Loaned;
        var error = await Assert.ThrowsAsync<AppException>(() => Service.ReserveAsync(book.Id,
            new() { Version = scenario == "stale" ? Guid.NewGuid() : book.Version }, actor, default));
        Assert.Equal(status, error.Status);
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        unit.Verify(x => x.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }
    [Fact]
    public async Task Other_reader_cannot_cancel_reservation()
    {
        book.Status = BookStatus.Reserved; book.ReservedById = reader.Id;
        var error = await Assert.ThrowsAsync<AppException>(() => Service.CancelReservationAsync(book.Id,
            new() { Version = book.Version }, new Actor(Guid.NewGuid(), false), default));
        Assert.Equal(403, error.Status);
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
