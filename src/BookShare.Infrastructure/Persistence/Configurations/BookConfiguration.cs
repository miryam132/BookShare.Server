using BookShare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookShare.Infrastructure.Persistence.Configurations;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(300);
        builder.Property(x => x.Author).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.City).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.HasOne(x => x.Owner).WithMany(x => x.Books)
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
