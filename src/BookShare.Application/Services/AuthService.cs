using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;

namespace BookShare.Application.Services;

public sealed class AuthService(IUserRepository users, IUnitOfWork unitOfWork,
    IPasswordSecurity passwords, ITokenIssuer tokens, IMapper mapper) : IAuthService
{
    public async Task<UserResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var normalized = email.ToUpperInvariant();
        if (await users.FindByNormalizedEmailAsync(normalized, cancellationToken) is not null)
            throw new AppException(409, "email_conflict", "Email is already registered.");
        var user = new User
        {
            FullName = request.FullName.Trim(), Email = email, NormalizedEmail = normalized,
            ContactPhone = request.ContactPhone.Trim(), Role = UserRole.Member
        };
        user.PasswordHash = passwords.Hash(user, request.Password);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return mapper.Map<UserResponse>(user);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByNormalizedEmailAsync(request.Email.Trim().ToUpperInvariant(), cancellationToken, tracking: true);
        if (user is null || !passwords.Verify(user, request.Password, out var rehash))
            throw new AppException(401, "invalid_credentials", "Email or password is incorrect.");
        if (rehash)
        {
            user.PasswordHash = passwords.Hash(user, request.Password);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return tokens.Issue(user);
    }

    public async Task<UserResponse> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new AppException(401, "invalid_credentials", "The authenticated user no longer exists.");
        return mapper.Map<UserResponse>(user);
    }
}
