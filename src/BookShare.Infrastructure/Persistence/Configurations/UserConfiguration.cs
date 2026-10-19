using BookShare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookShare.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Email).IsRequired().HasMaxLength(254);
        builder.Property(x => x.NormalizedEmail).IsRequired().HasMaxLength(254);
        builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ContactPhone).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Role).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}
