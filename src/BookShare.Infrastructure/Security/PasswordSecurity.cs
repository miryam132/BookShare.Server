using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace BookShare.Infrastructure.Security;

public sealed class PasswordSecurity : IPasswordSecurity
{
    private readonly PasswordHasher<User> hasher = new();
    public string Hash(User user, string password) => hasher.HashPassword(user, password);
    public bool Verify(User user, string password, out bool needsRehash)
    {
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }
}
