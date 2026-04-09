---
name: Project context
description: Distributed .NET 10 order processing system for academic assignment, student Dora #73262
type: project
---

Academic assignment for student Dora, student number 73262. Full Stack Development module, 30% weighting.

**Why:** Assignment requires a distributed order processing system called SportsStore demonstrating event-driven architecture.

**How to apply:** All new code goes in `11 - SportsStore - 5/End of Chapter/OrderProcessing/`. The original monolithic SportsStore MVC app remains in `11 - SportsStore - 5/End of Chapter/SportsSln/` and is kept for its own CI job.

## Solution layout
```
OrderProcessing/
  OrderProcessing.sln
  src/
    Shared.Domain/          — OrderStatus enum, domain events
    OrderManagement.API/    — REST API, EF Core, RabbitMQ publisher
    InventoryService/       — Worker, RabbitMQ consumer
    PaymentService/         — Worker, RabbitMQ consumer
    ShippingService/        — Worker, RabbitMQ consumer
    CustomerPortal/         — Blazor Server (cart, checkout, my orders)
    AdminDashboard/         — Blazor Server (filters, sorting, revenue)
  tests/
    OrderManagement.Tests/  — xUnit + Moq + EF InMemory
  docker-compose.yml
```

## Order state machine
Submitted → InventoryPending → InventoryConfirmed/InventoryFailed
→ PaymentPending → PaymentApproved/PaymentFailed
→ ShippingPending → ShippingCreated → Completed / Failed

## Key design decisions
- JsonStringEnumConverter on both API and client (fixes Task A deserialization bug)
- RabbitMQ workers fall back to no-op mode when RabbitMQ:Host is empty (safe for CI)
- Inventory store seeded with out-of-stock (products 3,8) and low-stock (2,5,7) items
- EF Core seeded with 11 orders covering every OrderStatus for Admin Dashboard demo
- CI has two jobs: one for SportsSln, one for OrderProcessing.sln
