using BookShare.Application;
using BookShare.Infrastructure;
using BookShare.API.Middleware;
using NLog.Web;
using BookShare.Infrastructure.Security;
using Microsoft.OpenApi;
using BookShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Maintenance commands never start HTTP, require an explicit target database name,
// and suppress provider output (including connection/SQL details).
if (args.Length > 0 && args[0].StartsWith("--maintenance", StringComparison.Ordinal))
{
    builder.Logging.ClearProviders();
    var maintenanceStage = "configuration";
    try
    {
        if (args.Length != 1 || args[0] is not ("--maintenance-migrate" or "--maintenance-seed"))
            throw new InvalidOperationException();
        var connection = builder.Configuration.GetConnectionString("BookShare");
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        var target = builder.Configuration["Maintenance:Database"];
        if (string.IsNullOrWhiteSpace(target) || target != cs.Database) throw new InvalidOperationException();
        cs.IncludeErrorDetail = false;
        await using var db = new BookShareDbContext(new DbContextOptionsBuilder<BookShareDbContext>()
            .UseNpgsql(cs.ConnectionString).Options);
        maintenanceStage = args[0] == "--maintenance-migrate" ? "migration" : "seed";
        if (args[0] == "--maintenance-migrate") await db.Database.MigrateAsync();
        else await DemoSeed.RunAsync(db, builder.Configuration);
        Console.WriteLine("Maintenance completed successfully.");
    }
    catch (Exception exception)
    {
        // Only fixed codes are emitted. Never print exception messages, SQL, connection
        // strings, provider Detail fields or stack traces in maintenance output.
        var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
        var code = cause switch
        {
            Npgsql.PostgresException { SqlState: "28P01" } => "database_password_rejected",
            Npgsql.PostgresException { SqlState: "28000" } => "database_access_rejected",
            Npgsql.PostgresException { SqlState: "42P01" } => "schema_missing",
            Npgsql.PostgresException { SqlState: "23505" } => "unique_constraint",
            Npgsql.PostgresException { SqlState: "23503" } => "foreign_key_constraint",
            Npgsql.PostgresException => "database_rejected_operation",
            Npgsql.NpgsqlException => "database_connection_failed",
            TimeoutException => "database_timeout",
            InvalidOperationException { Message: "Demo seed must be explicitly enabled." } => "seed_gate_disabled",
            InvalidOperationException { Message: "Supply four distinct demo passwords, 16-128 characters each, through secrets." } => "seed_password_requirements",
            InvalidOperationException { Message: "Apply reviewed migrations before seeding." } => "pending_migrations",
            InvalidOperationException { Message: "Demo identity collision; no accounts were changed." } => "demo_identity_collision",
            _ => "maintenance_configuration_or_operation_failed"
        };
        Console.Error.WriteLine($"Maintenance diagnostic: stage={maintenanceStage}; code={code}.");
        Console.Error.WriteLine("Maintenance failed. Check target confirmation, secrets, schema and demo identity collisions locally. No sensitive details are printed.");
        Environment.ExitCode = 1;
    }
    return;
}

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    || uri.Scheme is not ("http" or "https") || uri.GetLeftPart(UriPartial.Authority) != origin))
    throw new InvalidOperationException("CORS origins must be exact HTTP(S) origins without paths or trailing slashes.");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (origins.Length > 0) policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Authorization", "Content-Type").WithExposedHeaders("X-Correlation-ID");
}));

builder.Logging.ClearProviders();
builder.Host.UseNLog();
builder.Services.AddApiControllers();
builder.Services.AddApplication();
builder.Services.AddOptions<BookShare.Application.DTOs.LoanOptions>()
    .Bind(builder.Configuration.GetSection(BookShare.Application.DTOs.LoanOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBookShareSecurity(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

app.UseApiErrors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else if (!builder.Configuration.GetValue<bool>("Hosting:BehindTlsProxy"))
{
    app.UseHttpsRedirection();
}

// Render terminates HTTPS at its edge; no untrusted forwarded headers are accepted.
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
