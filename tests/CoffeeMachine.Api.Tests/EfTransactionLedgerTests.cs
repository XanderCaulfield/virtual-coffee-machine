using CoffeeMachine.Api.Data;
using CoffeeMachine.Domain.Ledger;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeMachine.Api.Tests;

/// <summary>
/// Verifies the EF-backed ledger directly, including the ambient-capture
/// path the machine registry uses to commit ledger rows atomically with the
/// machine row.
/// </summary>
public sealed class EfTransactionLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly EfTransactionLedger _ledger;

    public EfTransactionLedgerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<EfTransactionLedger>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

        _ledger = _provider.GetRequiredService<EfTransactionLedger>();
    }

    private static Transaction Row(string machineId, string itemId = "latte") =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, machineId, itemId, 300, 0, TransactionStatus.Purchased);

    [Fact]
    public void Append_outside_an_ambient_scope_persists_immediately()
    {
        _ledger.Append(Row("machine-a"));

        var rows = _ledger.List("machine-a", 10).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("machine-a", row.MachineId);
        Assert.Equal("latte", row.ItemId);
    }

    [Fact]
    public void Append_inside_an_ambient_scope_is_flushed_by_the_capturing_context()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        using (_ledger.Capture(db))
        {
            _ledger.Append(Row("machine-a"));
            // Not yet persisted: the row belongs to the ambient context.
            Assert.Empty(_ledger.List("machine-a", 10));

            db.SaveChanges();
        }

        Assert.Single(_ledger.List("machine-a", 10));
    }

    [Fact]
    public void List_returns_newest_first_and_respects_the_limit()
    {
        _ledger.Append(Row("machine-a"));
        Thread.Sleep(15);
        _ledger.Append(Row("machine-a", "decaf"));
        Thread.Sleep(15);
        _ledger.Append(Row("machine-b"));

        var rows = _ledger.List(null, 2).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("machine-b", rows[0].MachineId);
        Assert.Equal("decaf", rows[1].ItemId);
    }

    [Fact]
    public void List_filters_by_machine_id_when_given_one()
    {
        _ledger.Append(Row("machine-a"));
        _ledger.Append(Row("machine-b"));

        Assert.Single(_ledger.List("machine-a", 10));
        Assert.Equal(2, _ledger.List(null, 10).Count());
        Assert.Empty(_ledger.List("  ", 0)); // non-positive limit short-circuits
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
