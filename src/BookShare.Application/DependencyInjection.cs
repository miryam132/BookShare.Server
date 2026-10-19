using Microsoft.Extensions.DependencyInjection;

namespace BookShare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<DTOs.LoanOptions>();
        services.AddScoped<Interfaces.ILoanService, Services.LoanService>();
        services.AddScoped<Interfaces.IBookService, Services.BookService>();
        services.AddScoped<Interfaces.ICategoryService, Services.CategoryService>();
        services.AddScoped<Interfaces.IAuthService, Services.AuthService>();
        services.AddAutoMapper(options =>
        {
            var licenseKey = Environment.GetEnvironmentVariable("AUTOMAPPER_LICENSE_KEY");
            if (!string.IsNullOrWhiteSpace(licenseKey)) options.LicenseKey = licenseKey;
        }, typeof(DependencyInjection).Assembly);
        return services;
    }
}
