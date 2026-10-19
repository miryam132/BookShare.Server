using BookShare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BookShare.Infrastructure.Persistence.Configurations;
public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> b)
    {
        b.HasKey(x => x.Id);
        b.ToTable("Loans", t => {
            t.HasCheckConstraint("CK_Loans_DueAtUtc", "\"DueAtUtc\" > \"BorrowedAtUtc\"");
            t.HasCheckConstraint("CK_Loans_ReturnedAtUtc", "\"ReturnedAtUtc\" IS NULL OR \"ReturnedAtUtc\" >= \"BorrowedAtUtc\"");
        });
        b.HasIndex(x => x.BookId).IsUnique().HasDatabaseName("IX_Loans_ActiveBook").HasFilter("\"ReturnedAtUtc\" IS NULL");
        b.HasOne(x => x.Book).WithMany(x => x.Loans).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Borrower).WithMany(x => x.Loans).HasForeignKey(x => x.BorrowerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RecordedByUser).WithMany(x => x.RecordedLoans).HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
