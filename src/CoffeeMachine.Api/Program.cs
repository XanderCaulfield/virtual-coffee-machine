using CoffeeMachine.Api.Data;
using CoffeeMachine.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// RFC 7807 problem details for unhandled exceptions and validation failures.
builder.Services.AddProblemDetails();

// SQLite persistence: the connection string comes from config
// `ConnectionStrings:Default`, then the DB_PATH environment variable, then a
// default database file under the content root.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = ResolveDatabaseConnectionString(builder.Environment.ContentRootPath);
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

// The ledger and machine registry are singletons; they reach the scoped
// DbContext through IServiceScopeFactory.
builder.Services.AddSingleton<EfTransactionLedger>();
builder.Services.AddSingleton<ITransactionLedger>(sp => sp.GetRequiredService<EfTransactionLedger>());
builder.Services.AddSingleton<MachineRegistry>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapGet("/healthz", () => Results.Text("OK"));

// --- Blazor WebAssembly client hosting ---
// Serve the compiled client (wwwroot/_framework) and its static assets,
// then fall back to index.html for any non-API route so client-side
// routing (if added later) keeps working on refresh.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Create the SQLite schema (idempotent) before serving the first request.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();

/// <summary>
/// Resolves the SQLite connection string from the DB_PATH environment
/// variable, or falls back to a database file under the content root.
/// </summary>
static string ResolveDatabaseConnectionString(string contentRoot)
{
    var dbPath = Environment.GetEnvironmentVariable("DB_PATH");
    if (string.IsNullOrWhiteSpace(dbPath))
    {
        return $"Data Source={Path.Combine(contentRoot, "coffeemachine.db")}";
    }

    return dbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
        ? dbPath
        : $"Data Source={dbPath}";
}

/// <summary>Required by <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.</summary>
public partial class Program
{
}
