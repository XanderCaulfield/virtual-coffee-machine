using Microsoft.EntityFrameworkCore;

namespace CoffeeMachine.Api.Data;

/// <summary>
/// EF Core context for the SQLite persistence store.
/// Holds the append-only transaction ledger and one row per vending machine.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context with the given options (connection string).</summary>
    /// <param name="options">Configured by <c>AddDbContext</c> in Program.cs.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>The append-only transaction ledger.</summary>
    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();

    /// <summary>One row per machine: persisted state, balance and stock JSON.</summary>
    public DbSet<MachineEntity> Machines => Set<MachineEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionEntity>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.MachineId).HasMaxLength(64).IsRequired();
            entity.Property(t => t.ItemId).HasMaxLength(64).IsRequired();
            entity.Property(t => t.Status).HasMaxLength(16).IsRequired();

            // SQLite cannot ORDER BY DateTimeOffset (stored as TEXT), so the
            // timestamp is persisted as UTC ticks (INTEGER). Ordering by the
            // monotonic tick value is chronologically equivalent and lets the
            // ledger page with "newest first" inside the database.
            entity.Property(t => t.Timestamp)
                .HasConversion(
                    timestamp => timestamp.UtcTicks,
                    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

            entity.HasIndex(t => new { t.MachineId, t.Timestamp });
        });

        modelBuilder.Entity<MachineEntity>(entity =>
        {
            entity.ToTable("Machines");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasMaxLength(64).IsRequired();
            entity.Property(m => m.State).HasMaxLength(32).IsRequired();
            entity.Property(m => m.CoinStockJson).HasColumnType("TEXT").IsRequired();
            entity.Property(m => m.ItemStockJson).HasColumnType("TEXT").IsRequired();
        });
    }
}
