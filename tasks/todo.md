# Assignment Task Tracker — Dora (Student 73262)

## Status Key
- [x] Done   [ ] Pending   [~] In Progress

---

## Critical Bug Fixes

- [x] **Task A — My Orders deserialization bug**
  - Root cause: API serialised `OrderStatus` as integer (e.g. `9`); client expected string (`"Completed"`)
  - Fix: `JsonStringEnumConverter` added to `OrderManagement.API/Program.cs` → `AddJsonOptions`
  - Fix: Same converter added to `CustomerPortal/Services/OrderApiService.cs` → `_opts`
  - Regression test: `OrderStatusSerializationTests.cs` → `Client_WithoutConverter_ThrowsOnStringStatus`
  - Files: `Program.cs`, `OrderApiService.cs`, `OrderStatusSerializationTests.cs`

- [x] **Architecture gap — Workers publish events but nobody updates order status**
  - Root cause: InventoryService/PaymentService/ShippingService publish to RabbitMQ exchanges but
    OrderManagement.API had no consumer. Orders were stuck at InventoryPending forever.
  - Fix: Added `OrderManagement.API/Workers/OrderEventConsumer.cs` — a single BackgroundService
    that subscribes to all 5 result exchanges and updates order status via scoped DbContext.
  - Falls back to no-op when `RabbitMQ:Host` is empty (CI-safe).

- [x] **State machine gaps (double-save transitions)**
  - `InventoryConfirmed` endpoint: InventoryPending → InventoryConfirmed → PaymentPending (two saves)
  - `PaymentApproved` endpoint: PaymentPending → PaymentApproved → ShippingPending (two saves)
  - `ShippingCreated` endpoint: ShippingPending → ShippingCreated → Completed (two saves)

- [x] **OrderConfirmation.razor broken FetchById**
  - Old: used `new HttpClient()` with no base URL → always null
  - Fix: calls `Api.GetOrderByIdAsync(OrderId)` via injected service

---

## Part B — Serilog
- [x] `WithMachineName()`, `WithEnvironmentName()`, `WithThreadId()`, `FromLogContext()`
- [x] Console + rolling File sinks in all 6 services
- [x] `appsettings.Development.json` → Debug level
- [x] `appsettings.Production.json` → Information + Warning overrides
- [x] Structured fields: OrderId, CustomerId, EventType in log messages

---

## Part D — CI Pipeline (.github/workflows/ci.yml)
- [x] Triggers on push/PR to `master` (corrected from `main`)
- [x] Job `sportsstore` → builds & tests `SportsSln.sln`
- [x] Job `order-processing` → builds & tests `OrderProcessing.sln`
- [x] `dotnet test --logger trx --collect "XPlat Code Coverage"`
- [x] Pipeline fails automatically on test failure

---

## Feature Implementation

### Task A — Cart & Checkout
- [x] Cart badge: reactive bubble on navbar cart icon via `CartService.StateChanged`
- [x] Cart page: table, qty stepper, remove, subtotal
- [x] Checkout: form with customerId/name/address, order summary, inventory pre-check
- [x] Stock levels: "In stock" / "Only X left!" / "Out of stock" + disabled button
- [x] Inventory re-check before order placement (prevents race conditions)

### Task B — Admin Dashboard
- [x] Status filter dropdown (all 11 statuses)
- [x] Customer ID search filter
- [x] Sortable columns: Customer, Date, Total, Status
- [x] Failed orders visible with FailureReason in filter
- [x] Revenue = Completed orders only (`AdminController.GetStats`)
- [x] Auto-refresh every 15 seconds

### Task C — Simulation
- [x] Inventory seeded: products 3 & 8 = 0 stock, products 2/5/7 = low stock (1–3)
- [x] InventoryService.TryReserve: fails if qty > available
- [x] PaymentService: 90% success, 10% random failure ("Card declined")
- [x] ShippingService: always succeeds (tracking number generated)

### Task D — My Orders
- [x] Customer ID lookup (no login system)
- [x] Status badges: colour-coded (Submitted/Progress/Completed/Failed)
- [x] Filter buttons: All / Completed / Failed / In Progress
- [x] Sorting: Date, Total, Status (click headers, asc/desc toggle)

### Task E — Admin Orders
- [x] Status badge colour groups: Blue/Pink/Yellow/Gray
- [x] Sortable headers with ↑↓⇅ icons
- [x] Revenue footer (completed only)

### Task F — Seed Data
- [x] 11 seed orders covering every `OrderStatus` value
- [x] Seed via `HasData()` in `OnModelCreating`, created with `EnsureCreated()`

### Task G — Tests
- [x] `OrdersControllerTests`: creation, event publishing, state transitions, filters, not-found
- [x] `OrderStatusSerializationTests`: regression proof of deserialization bug
- [x] `InventoryStoreTests`: stock reservation logic

---

## Apple UI Theme
- [x] `-apple-system` font stack throughout
- [x] `#007AFF` accent, `#F2F2F7` background
- [x] Frosted-glass navbar (`backdrop-filter: blur(20px)`)
- [x] `border-radius: 12px` cards
- [x] Status badges, filter pills, qty stepper all Apple-styled
- [x] AdminDashboard: sidebar layout, metric cards grid

---

## Architecture Verification

End-to-end pipeline (with RabbitMQ running):
```
Customer Portal (Blazor)
  → POST /api/orders/checkout (OrderManagement.API)
  → publishes OrderSubmittedEvent to "order.submitted" (RabbitMQ)
  → InventoryService listens → reserves stock → publishes inventory.confirmed|failed
  → OrderEventConsumer (in API) listens → transitions order to PaymentPending|InventoryFailed
  → PaymentService listens → 90% success → publishes payment.approved|failed
  → OrderEventConsumer transitions → ShippingPending|PaymentFailed
  → ShippingService listens → generates tracking → publishes shipping.created
  → OrderEventConsumer transitions → Completed
Admin Dashboard reads from same Orders DB → accurate metrics
```
