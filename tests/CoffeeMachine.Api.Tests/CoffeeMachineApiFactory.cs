using CoffeeMachine.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CoffeeMachine.Api.Tests;

/// <summary>
/// Boots the real API with an isolated SQLite database file per test run, so
/// every test class gets a pristine ledger and machine store that is deleted
/// when the factory is disposed.
/// </summary>
public sealed class CoffeeMachineApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"coffeemachine-api-tests-{Guid.NewGuid():N}.db");

    /// <summary>The path of the isolated SQLite database file.</summary>
    public string DbPath => _dbPath;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            }));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // Release pooled connections so the database file can be deleted.
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // Best effort: temp files are harmless if they linger.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort: temp files are harmless if they linger.
            }
        }
    }
}
