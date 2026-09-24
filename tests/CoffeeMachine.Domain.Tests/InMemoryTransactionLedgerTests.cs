using CoffeeMachine.Domain.Ledger;

namespace CoffeeMachine.Domain.Tests;

/// <summary>Verifies the thread-safe in-memory ledger used by tests and fallback scenarios.</summary>
public class InMemoryTransactionLedgerTests
{
    private static Transaction TransactionFor(string machineId, string status = TransactionStatus.Purchased) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, machineId, "latte", 300, 0, status);

    [Fact]
    public void List_filters_by_machine_id()
    {
        var ledger = new InMemoryTransactionLedger();
        ledger.Append(TransactionFor("machine-a"));
        ledger.Append(TransactionFor("machine-b"));
        ledger.Append(TransactionFor("machine-a", TransactionStatus.Cancelled));

        var rows = ledger.List("machine-a", 100).ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("machine-a", row.MachineId));
    }

    [Fact]
    public void List_returns_newest_first()
    {
        var ledger = new InMemoryTransactionLedger();
        var first = TransactionFor("machine-a");
        var second = TransactionFor("machine-a");
        var third = TransactionFor("machine-a");
        ledger.Append(first);
        ledger.Append(second);
        ledger.Append(third);

        var rows = ledger.List("machine-a", 100).ToList();

        Assert.Equal(new[] { third.Id, second.Id, first.Id }, rows.Select(row => row.Id));
    }

    [Fact]
    public void List_respects_the_limit()
    {
        var ledger = new InMemoryTransactionLedger();
        for (var i = 0; i < 5; i++)
        {
            ledger.Append(TransactionFor("machine-a"));
        }

        var rows = ledger.List("machine-a", 2).ToList();

        Assert.Equal(2, rows.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void List_returns_nothing_for_non_positive_limits(int limit)
    {
        var ledger = new InMemoryTransactionLedger();
        ledger.Append(TransactionFor("machine-a"));

        Assert.Empty(ledger.List("machine-a", limit));
    }

    [Fact]
    public void List_returns_nothing_for_unknown_machines()
    {
        var ledger = new InMemoryTransactionLedger();
        ledger.Append(TransactionFor("machine-a"));

        Assert.Empty(ledger.List("machine-b", 100));
    }

    [Fact]
    public void Append_is_thread_safe()
    {
        var ledger = new InMemoryTransactionLedger();
        const int perThread = 250;

        var threads = new List<Thread>();
        for (var threadIndex = 0; threadIndex < 4; threadIndex++)
        {
            var machineId = $"machine-{threadIndex}";
            threads.Add(new Thread(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    ledger.Append(TransactionFor(machineId));
                }
            }));
        }

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        for (var threadIndex = 0; threadIndex < 4; threadIndex++)
        {
            Assert.Equal(perThread, ledger.List($"machine-{threadIndex}", int.MaxValue).Count());
        }
    }
}
