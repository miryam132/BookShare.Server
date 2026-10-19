using BookShare.Infrastructure.Repositories;
using BookShare.Application.Interfaces;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookShare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BookShare");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set the ConnectionStrings__BookShare environment variable before starting the API.");
        }

        services.AddDbContext<BookShareDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDatabaseHealthCheck, DatabaseHealthCheck>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
