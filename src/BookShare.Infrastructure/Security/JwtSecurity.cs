using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookShare.Infrastructure.Security;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 60;
    public SymmetricSecurityKey Key => new(Convert.FromBase64String(SigningKey));
}

public sealed class JwtTokenIssuer(JwtSettings settings) : ITokenIssuer
{
    public TokenResponse Issue(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim("sub", user.Id.ToString()), new Claim("role", user.Role.ToString()),
             new Claim("jti", Guid.NewGuid().ToString())], now, expires,
            new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256));
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires);
    }
}

public static class JwtSecurity
{
    public static IServiceCollection AddBookShareSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new();
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience)
            || settings.LifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Configure Jwt Issuer, Audience and LifetimeMinutes (1-1440).");
        try
        {
            if (settings.Key.KeySize < 256) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Set Jwt__SigningKey to a Base64-encoded random key of at least 32 bytes.");
        }
        services.AddSingleton(settings);
        services.AddScoped<IPasswordSecurity, PasswordSecurity>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = settings.Key,
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero, NameClaimType = "sub", RoleClaimType = "role",
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            };
        });
        services.AddAuthorization();
        return services;
    }
}
