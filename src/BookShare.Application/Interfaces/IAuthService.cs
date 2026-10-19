using BookShare.Application.DTOs;
using BookShare.Domain.Entities;

namespace BookShare.Application.Interfaces;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken);
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserResponse> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IPasswordSecurity
{
    string Hash(User user, string password);
    bool Verify(User user, string password, out bool needsRehash);
}

public interface ITokenIssuer
{
    TokenResponse Issue(User user);
}
