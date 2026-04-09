using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data.Entities;
using Shared.Domain.Enums;

namespace OrderManagement.API.Data;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            e.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
            // LineTotal is computed, not stored
            e.Ignore(l => l.LineTotal);
        });

        // Seed demo orders covering every status for the Admin Dashboard
        SeedOrders(modelBuilder);
    }

    private static void SeedOrders(ModelBuilder modelBuilder)
    {
        var now = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        // ── Fixed GUIDs so EF HasData is idempotent across restarts ─────────────
        // Orders cover all 11 statuses so the Admin Dashboard shows a varied table.
        var orders = new[]
        {
            // Status: Submitted — Kayak ordered by Alice
            new { Id = new Guid("a1b2c3d4-0001-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0001000-0000-4000-8000-000000000001"), CustomerId = "CUST001", CustomerName = "Alice Martin",  ShippingAddress = "1 Main St, Dublin, D01 AB12",    TotalAmount = 275.00m,  Status = OrderStatus.Submitted,         FailureReason = (string?)null,                       TrackingNumber = (string?)null,     PaymentTransactionId = (string?)null,          CreatedAt = now.AddHours(-48), UpdatedAt = now.AddHours(-48) },

            // Status: InventoryPending — Lifejacket waiting stock check
            new { Id = new Guid("a1b2c3d4-0002-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0002000-0000-4000-8000-000000000002"), CustomerId = "CUST002", CustomerName = "Bob Smith",    ShippingAddress = "2 River Rd, Cork, T12 CD34",     TotalAmount = 48.95m,   Status = OrderStatus.InventoryPending,  FailureReason = (string?)null,                       TrackingNumber = (string?)null,     PaymentTransactionId = (string?)null,          CreatedAt = now.AddHours(-44), UpdatedAt = now.AddHours(-44) },

            // Status: InventoryConfirmed — Stadium confirmed in warehouse
            new { Id = new Guid("a1b2c3d4-0003-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0003000-0000-4000-8000-000000000003"), CustomerId = "CUST003", CustomerName = "Carol White",  ShippingAddress = "3 Lake Ave, Galway, H91 EF56",   TotalAmount = 79500.00m, Status = OrderStatus.InventoryConfirmed, FailureReason = (string?)null,                      TrackingNumber = (string?)null,     PaymentTransactionId = (string?)null,          CreatedAt = now.AddHours(-40), UpdatedAt = now.AddHours(-40) },

            // Status: InventoryFailed — Soccer Ball out of stock
            new { Id = new Guid("a1b2c3d4-0004-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0004000-0000-4000-8000-000000000004"), CustomerId = "CUST004", CustomerName = "Dan Jones",    ShippingAddress = "4 Hill St, Limerick, V94 GH78",  TotalAmount = 19.50m,   Status = OrderStatus.InventoryFailed,   FailureReason = (string?)"Soccer Ball is not available in the requested quantity", TrackingNumber = (string?)null, PaymentTransactionId = (string?)null, CreatedAt = now.AddHours(-36), UpdatedAt = now.AddHours(-36) },

            // Status: PaymentPending — Corner Flags awaiting payment gateway
            new { Id = new Guid("a1b2c3d4-0005-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0005000-0000-4000-8000-000000000005"), CustomerId = "CUST005", CustomerName = "Eve Brown",    ShippingAddress = "5 Oak Rd, Waterford, X91 IJ90",  TotalAmount = 34.95m,   Status = OrderStatus.PaymentPending,    FailureReason = (string?)null,                       TrackingNumber = (string?)null,     PaymentTransactionId = (string?)null,          CreatedAt = now.AddHours(-32), UpdatedAt = now.AddHours(-32) },

            // Status: PaymentApproved — Thinking Cap payment approved
            new { Id = new Guid("a1b2c3d4-0006-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0006000-0000-4000-8000-000000000006"), CustomerId = "CUST001", CustomerName = "Alice Martin",  ShippingAddress = "1 Main St, Dublin, D01 AB12",    TotalAmount = 16.00m,   Status = OrderStatus.PaymentApproved,   FailureReason = (string?)null,                       TrackingNumber = (string?)null,     PaymentTransactionId = (string?)"TXN-20260401-0006", CreatedAt = now.AddHours(-28), UpdatedAt = now.AddHours(-28) },

            // Status: PaymentFailed — Unsteady Chair card declined
            new { Id = new Guid("a1b2c3d4-0007-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0007000-0000-4000-8000-000000000007"), CustomerId = "CUST002", CustomerName = "Bob Smith",    ShippingAddress = "2 River Rd, Cork, T12 CD34",     TotalAmount = 29.95m,   Status = OrderStatus.PaymentFailed,     FailureReason = (string?)"Card declined (simulated)",  TrackingNumber = (string?)null,     PaymentTransactionId = (string?)null,          CreatedAt = now.AddHours(-24), UpdatedAt = now.AddHours(-24) },

            // Status: ShippingPending — Human Chess Board en route to courier
            new { Id = new Guid("a1b2c3d4-0008-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0008000-0000-4000-8000-000000000008"), CustomerId = "CUST003", CustomerName = "Carol White",  ShippingAddress = "3 Lake Ave, Galway, H91 EF56",   TotalAmount = 75.00m,   Status = OrderStatus.ShippingPending,   FailureReason = (string?)null,                       TrackingNumber = (string?)null,     PaymentTransactionId = (string?)"TXN-20260401-0008", CreatedAt = now.AddHours(-20), UpdatedAt = now.AddHours(-20) },

            // Status: ShippingCreated — Bling-Bling King label printed
            new { Id = new Guid("a1b2c3d4-0009-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0009000-0000-4000-8000-000000000009"), CustomerId = "CUST004", CustomerName = "Dan Jones",    ShippingAddress = "4 Hill St, Limerick, V94 GH78",  TotalAmount = 1200.00m, Status = OrderStatus.ShippingCreated,   FailureReason = (string?)null,                       TrackingNumber = (string?)"TRK-20260401-9001", PaymentTransactionId = (string?)"TXN-20260401-0009", CreatedAt = now.AddHours(-16), UpdatedAt = now.AddHours(-16) },

            // Status: Completed — Whistle delivered
            new { Id = new Guid("a1b2c3d4-0010-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0010000-0000-4000-8000-000000000010"), CustomerId = "CUST005", CustomerName = "Eve Brown",    ShippingAddress = "5 Oak Rd, Waterford, X91 IJ90",  TotalAmount = 12.50m,   Status = OrderStatus.Completed,         FailureReason = (string?)null,                       TrackingNumber = (string?)"TRK-20260401-9002", PaymentTransactionId = (string?)"TXN-20260401-0010", CreatedAt = now.AddHours(-12), UpdatedAt = now.AddHours(-8) },

            // Status: Failed — Kayak + Lifejacket combo, payment processor down
            new { Id = new Guid("a1b2c3d4-0011-4000-8000-aabbccddeeff"), CorrelationId = new Guid("c0011000-0000-4000-8000-000000000011"), CustomerId = "CUST001", CustomerName = "Alice Martin",  ShippingAddress = "1 Main St, Dublin, D01 AB12",    TotalAmount = 323.95m,  Status = OrderStatus.Failed,            FailureReason = (string?)"Payment processor unavailable", TrackingNumber = (string?)null, PaymentTransactionId = (string?)null, CreatedAt = now.AddHours(-6), UpdatedAt = now.AddHours(-6) },
        };

        modelBuilder.Entity<Order>().HasData(orders);

        // ── One or two order lines per order, using the real store product catalogue ──
        // Products: 1=Kayak $275, 2=Lifejacket $48.95, 3=Soccer Ball $19.50,
        //           4=Corner Flags $34.95, 5=Stadium $79500, 6=Thinking Cap $16,
        //           7=Unsteady Chair $29.95, 8=Human Chess Board $75, 9=Bling-Bling King $1200,
        //           10=Whistle $12.50
        var lines = new object[]
        {
            // Order 1 – Submitted: 1x Kayak
            new { Id = new Guid("b1000001-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0001-4000-8000-aabbccddeeff"), ProductId = 1L, ProductName = "Kayak",             Quantity = 1, UnitPrice = 275.00m },

            // Order 2 – InventoryPending: 1x Lifejacket
            new { Id = new Guid("b1000002-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0002-4000-8000-aabbccddeeff"), ProductId = 2L, ProductName = "Lifejacket",        Quantity = 1, UnitPrice = 48.95m  },

            // Order 3 – InventoryConfirmed: 1x Stadium
            new { Id = new Guid("b1000003-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0003-4000-8000-aabbccddeeff"), ProductId = 5L, ProductName = "Stadium",           Quantity = 1, UnitPrice = 79500.00m },

            // Order 4 – InventoryFailed: 1x Soccer Ball (out of stock)
            new { Id = new Guid("b1000004-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0004-4000-8000-aabbccddeeff"), ProductId = 3L, ProductName = "Soccer Ball",       Quantity = 1, UnitPrice = 19.50m  },

            // Order 5 – PaymentPending: 1x Corner Flags
            new { Id = new Guid("b1000005-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0005-4000-8000-aabbccddeeff"), ProductId = 4L, ProductName = "Corner Flags",      Quantity = 1, UnitPrice = 34.95m  },

            // Order 6 – PaymentApproved: 1x Thinking Cap
            new { Id = new Guid("b1000006-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0006-4000-8000-aabbccddeeff"), ProductId = 6L, ProductName = "Thinking Cap",      Quantity = 1, UnitPrice = 16.00m  },

            // Order 7 – PaymentFailed: 1x Unsteady Chair
            new { Id = new Guid("b1000007-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0007-4000-8000-aabbccddeeff"), ProductId = 7L, ProductName = "Unsteady Chair",    Quantity = 1, UnitPrice = 29.95m  },

            // Order 8 – ShippingPending: 1x Human Chess Board
            new { Id = new Guid("b1000008-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0008-4000-8000-aabbccddeeff"), ProductId = 8L, ProductName = "Human Chess Board", Quantity = 1, UnitPrice = 75.00m  },

            // Order 9 – ShippingCreated: 1x Bling-Bling King
            new { Id = new Guid("b1000009-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0009-4000-8000-aabbccddeeff"), ProductId = 9L, ProductName = "Bling-Bling King",  Quantity = 1, UnitPrice = 1200.00m },

            // Order 10 – Completed: 1x Whistle
            new { Id = new Guid("b1000010-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0010-4000-8000-aabbccddeeff"), ProductId = 10L, ProductName = "Whistle",          Quantity = 1, UnitPrice = 12.50m  },

            // Order 11 – Failed: 1x Kayak + 1x Lifejacket (combo order)
            new { Id = new Guid("b1000011-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0011-4000-8000-aabbccddeeff"), ProductId = 1L, ProductName = "Kayak",             Quantity = 1, UnitPrice = 275.00m },
            new { Id = new Guid("b1000012-0000-4000-8000-aabbccddeeff"), OrderId = new Guid("a1b2c3d4-0011-4000-8000-aabbccddeeff"), ProductId = 2L, ProductName = "Lifejacket",        Quantity = 1, UnitPrice = 48.95m  },
        };

        modelBuilder.Entity<OrderLine>().HasData(lines);
    }
}
