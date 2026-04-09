# Engineering Report — Distributed Order Processing System
**Student:** Dora · Student Number 73262
**Date:** 2026-04-05

---

## 1. Bug Fixes

### Bug 1 — My Orders Deserialization Error (Task A)

**Error message:**
```
DeserializeUnableToConvertValue, System.String
Path: $[0].status | LineNumber: 0 | BytePositionInLine: 222.
```

**Root cause:**
`OrderStatus` is a C# enum. By default, `System.Text.Json` serialises enums as their **integer value** (e.g. `9` for `Completed`). The Blazor client's `HttpClient` was deserialising the JSON response without a converter, so when it encountered the string `"Completed"` (after the API fix) it threw `JsonException`. Alternatively, if the API was emitting integers the client model's `OrderStatus Status { get; set; }` would fail because `9` is not a valid deserialisation target when the client has a converter expecting a string.

**Files changed:**

| File | Change |
|------|--------|
| `OrderManagement.API/Program.cs` | Added `JsonStringEnumConverter` to `AddJsonOptions` |
| `CustomerPortal/Services/OrderApiService.cs` | Added `JsonStringEnumConverter` to `_opts` |
| `tests/OrderManagement.Tests/OrderStatusSerializationTests.cs` | Regression test: `Client_WithoutConverter_ThrowsOnStringStatus` proves original bug |

**Before (API):**
```csharp
builder.Services.AddControllers(); // No converter → Status emitted as integer
```

**After (API):**
```csharp
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// → Status now emitted as "Completed", "Submitted", etc.
```

**Before (Client):**
```csharp
private static readonly JsonSerializerOptions _opts = new()
{
    PropertyNameCaseInsensitive = true
    // No converter → throws when reading "Completed" as enum
};
```

**After (Client):**
```csharp
private static readonly JsonSerializerOptions _opts = new()
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }  // Fix: reads "Completed" → OrderStatus.Completed
};
```

---

### Bug 2 — Critical Architecture Gap: Orders Never Advance Past InventoryPending

**Root cause:**
The three worker microservices (InventoryService, PaymentService, ShippingService) **only publish events to RabbitMQ exchanges**. They do **not** call back to the OrderManagement.API. The API had HTTP PUT endpoints (`/inventory-confirmed`, `/payment-approved`, etc.) but nothing was calling them. This meant every order placed in production would remain permanently stuck at `InventoryPending`.

**File added:**
`OrderManagement.API/Workers/OrderEventConsumer.cs` — a `BackgroundService` that:
- Subscribes to all 5 RabbitMQ result exchanges: `inventory.confirmed`, `inventory.failed`, `payment.approved`, `payment.failed`, `shipping.created`
- For each message, creates a scoped `OrderDbContext` (via `IServiceScopeFactory`) and transitions order status in the database
- Falls back to no-op when `RabbitMQ:Host` is empty (CI / unit-test safe)

**File changed:**
`OrderManagement.API/Program.cs` — added `builder.Services.AddHostedService<OrderEventConsumer>()`

**State machine implemented (per event):**

| Exchange | From | To (intermediate) | To (final) |
|----------|------|-------------------|------------|
| `inventory.confirmed` | InventoryPending | InventoryConfirmed | PaymentPending |
| `inventory.failed` | InventoryPending | — | InventoryFailed |
| `payment.approved` | PaymentPending | PaymentApproved | ShippingPending |
| `payment.failed` | PaymentPending | — | PaymentFailed |
| `shipping.created` | ShippingPending | ShippingCreated | Completed |

---

### Bug 3 — OrderConfirmation.razor Broken FetchById

**Root cause:** Original code used `new HttpClient()` with no base URL configured, causing `GetOrderByIdAsync` to return `null` on every call.

**Fix:** Added `GetOrderByIdAsync(Guid)` method to `OrderApiService` using the injected `HttpClient` (which has the correct base URL from config). `OrderConfirmation.razor` now calls `Api.GetOrderByIdAsync(OrderId)`.

---

### Bug 4 — Test Assertions Wrong After State Machine Fix

**Root cause:** `OrdersControllerTests.cs` asserted `InventoryConfirmed` as the final state of the `InventoryConfirmed` endpoint, but after the double-save fix the endpoint ends at `PaymentPending`.

**Tests renamed and corrected:**
- `InventoryConfirmed_TransitionsFromInventoryPendingToConfirmed` → now asserts `OrderStatus.PaymentPending`
- `PaymentApproved_SetsTransactionId` → now asserts `OrderStatus.ShippingPending`

---

## 2. Features Implemented

### A. Customer Portal — Cart & Checkout

**Files:** `MainLayout.razor`, `Products.razor`, `Cart.razor`, `Checkout.razor`, `Services/CartService.cs`, `Services/OrderApiService.cs`

| Feature | Implementation |
|---------|---------------|
| Cart badge | Singleton `CartService` exposes `StateChanged` event; `MainLayout.razor` subscribes and re-renders `ap-cart-badge` reactively |
| Cart page | Apple-styled table with qty stepper (`−`, input, `+`), remove button, order total |
| Checkout form | Customer ID, Full Name, Shipping Address fields; order summary table |
| Inventory pre-check | `CheckInventoryAsync` called on page load AND before final submit; shows failure banners |
| Out-of-stock UI | Products 3 & 8 at 0 → disabled "Add to Cart" button; products 2/5/7 at 1–3 → "Only X left!" badge |

### B. My Orders (Task D)

**File:** `MyOrders.razor`

| Feature | Implementation |
|---------|---------------|
| Customer ID lookup | Text input + "Load Orders" button; calls `GET /api/customers/{id}/orders` |
| Status badges | `status-badge badge-submitted/completed/failed/progress` CSS classes |
| Filter pills | All / Completed / Failed / In Progress — client-side filtering |
| Sortable headers | Date, Total, Status — click to toggle asc/desc; active column shows ↑/↓ |

### C. Admin Dashboard (Task E)

**Files:** `AdminDashboard/Components/Pages/Dashboard.razor`, `AdminDashboard/Components/Pages/Orders.razor`

| Feature | Implementation |
|---------|---------------|
| Status filter | Dropdown with all 11 `OrderStatus` values → `GET /api/orders?status=X` |
| Customer ID filter | Input → `GET /api/orders?customerId=Y` |
| Sortable columns | Customer, Date, Total, Status; click headers → toggles sort direction |
| Failed orders | Visible via `InventoryFailed / PaymentFailed / Failed` filter; `FailureReason` in badge row |
| Revenue = Completed only | `AdminController.GetStats`: `completed.Sum(o => o.TotalAmount)` — failed/pending excluded |
| Auto-refresh | Dashboard: 10s; Orders: 15s; `Timer` with `InvokeAsync(StateHasChanged)` |
| Status badge colours | Blue=Submitted, Pink=Inventory/Payment, Yellow=Shipping/Completed, Gray=Failed |

### D. Serilog Structured Logging (Part B)

All 6 services (`OrderManagement.API`, `CustomerPortal`, `AdminDashboard`, `InventoryService`, `PaymentService`, `ShippingService`) are configured with:
- `WithMachineName()`, `WithEnvironmentName()`, `WithThreadId()`, `FromLogContext()`
- Console + daily rolling File sinks (7-day retention)
- Structured fields: `{OrderId}`, `{CustomerId}`, `{EventType}`, service name in category

### E. CI Pipeline (Part D)

**File:** `.github/workflows/ci.yml`
- Triggers on `push` and `pull_request` to `master` branch
- Job `sportsstore` → builds and tests `SportsSln.sln`
- Job `order-processing` → builds and tests `OrderProcessing.sln`
- `dotnet test --logger "trx;LogFileName=..."` + `--collect:"XPlat Code Coverage"`
- Pipeline fails automatically on any test failure (dotnet test exits non-zero)

---

## 3. Architecture Verification

### Complete Event Flow (with RabbitMQ running)

```
Customer adds items → clicks "Place Order"
  Customer Portal → POST /api/orders/checkout
    OrderManagement.API creates Order (status=Submitted)
    Publishes OrderSubmittedEvent → "order.submitted" exchange
    Sets order status = InventoryPending

  InventoryService (BackgroundService)
    Receives OrderSubmittedEvent from "inventory.order.submitted" queue
    Calls InventoryStore.TryReserve (thread-safe, all-or-nothing)
    → If OK: publishes InventoryConfirmedEvent → "inventory.confirmed"
    → If fail: publishes InventoryFailedEvent → "inventory.failed"

  OrderEventConsumer (in OrderManagement.API — THE MISSING LINK ADDED)
    Receives InventoryConfirmedEvent → sets order InventoryConfirmed → PaymentPending
    OR InventoryFailedEvent → sets order InventoryFailed (with reason)

  PaymentService (BackgroundService)
    Receives InventoryConfirmedEvent from "payment.inventory.confirmed" queue
    90% chance success: publishes PaymentApprovedEvent → "payment.approved"
    10% chance failure: publishes PaymentFailedEvent → "payment.failed"

  OrderEventConsumer
    Receives PaymentApprovedEvent → sets order PaymentApproved → ShippingPending
    OR PaymentFailedEvent → sets order PaymentFailed

  ShippingService (BackgroundService)
    Receives PaymentApprovedEvent from "shipping.payment.approved" queue
    Generates tracking number (TRK-{date}-{orderId})
    Publishes ShippingCreatedEvent → "shipping.created"

  OrderEventConsumer
    Receives ShippingCreatedEvent → sets order ShippingCreated → Completed

  Admin Dashboard reads from Orders DB → shows updated status + revenue
  Customer Portal "My Orders" → shows updated status with badge
```

### End-to-End Scenarios

**Scenario 1 — Successful Order**
1. Add Kayak (product 1, stock=10) to cart
2. Checkout with customerId=CUST001, name, address
3. Inventory check passes → order confirmed
4. Order Confirmation page shows status=InventoryPending
5. Admin Dashboard shows +1 order
6. With RabbitMQ: status advances → Completed within seconds
7. Admin Revenue metric increases

**Scenario 2 — Out-of-Stock / Inventory Failure**
1. Add Soccer Ball (product 3, stock=0) to cart
2. Checkout page shows "Soccer Ball – only 0 available" warning
3. "Place Order" button is disabled
4. Order is NOT submitted to API
5. Alternatively: add Lifejacket (stock=3), qty=5 → checkout pre-check catches it

**Scenario 3 — Payment Failure (Simulated)**
1. Place a valid order (in-stock items, qty ≤ available)
2. Order progresses to PaymentPending
3. PaymentService has 10% random failure rate
4. If failed: order moves to PaymentFailed with reason "Card declined (simulated)"
5. Admin Dashboard "Failed" counter increments
6. My Orders page shows "Failed" badge for this order
7. Revenue metric does NOT include this order

---

## 4. File Index

| File | Purpose |
|------|---------|
| `OrderManagement.API/Workers/OrderEventConsumer.cs` | **NEW** — RabbitMQ consumer that updates order status |
| `OrderManagement.API/Program.cs` | Registers `OrderEventConsumer`, Serilog, `JsonStringEnumConverter`, CORS |
| `OrderManagement.API/Controllers/OrdersController.cs` | State machine transitions (double-save pattern) |
| `OrderManagement.API/Controllers/AdminController.cs` | Stats: revenue = completed orders only |
| `OrderManagement.API/Controllers/InventoryController.cs` | Stock data; seeded with 0/low/normal levels |
| `OrderManagement.API/Data/OrderDbContext.cs` | 11 seed orders covering every `OrderStatus` |
| `CustomerPortal/Services/OrderApiService.cs` | `JsonStringEnumConverter` fix; `GetOrderByIdAsync` method |
| `CustomerPortal/Components/Layout/MainLayout.razor` | Frosted navbar, reactive cart badge |
| `CustomerPortal/Components/Pages/Products.razor` | Product grid, stock badges, add-to-cart |
| `CustomerPortal/Components/Pages/Cart.razor` | Cart table, qty stepper, subtotal |
| `CustomerPortal/Components/Pages/Checkout.razor` | Form, order summary, inventory pre-check |
| `CustomerPortal/Components/Pages/MyOrders.razor` | Filter pills, sortable table, status badges |
| `CustomerPortal/Components/Pages/OrderConfirmation.razor` | Fixed order lookup via `GetOrderByIdAsync` |
| `CustomerPortal/wwwroot/app.css` | Apple design system + all required CSS classes |
| `AdminDashboard/Components/Pages/Dashboard.razor` | Metric cards (revenue=completed only), auto-refresh |
| `AdminDashboard/Components/Pages/Orders.razor` | Status filter, customer filter, sortable columns |
| `AdminDashboard/wwwroot/app.css` | Apple sidebar design system |
| `tests/OrderManagement.Tests/OrdersControllerTests.cs` | State machine, CRUD, filter tests |
| `tests/OrderManagement.Tests/OrderStatusSerializationTests.cs` | Regression proof of Task A bug |
| `tests/OrderManagement.Tests/InventoryStoreTests.cs` | Stock reservation unit tests |
| `.github/workflows/ci.yml` | CI pipeline on `master` branch |
| `AdminDashboard/appsettings.json` | **FIXED** — BaseUrl corrected from invalid semicolon string to `https://localhost:7100/` |
| `CustomerPortal/appsettings.json` | **FIXED** — BaseUrl corrected from `https://localhost:7050/` to `https://localhost:7100/` |
| `OrderManagement.API/Program.cs` | CORS origins updated to include actual launchSettings ports |
| `tasks/todo.md` | Assignment task tracker |
| `tasks/lessons.md` | Engineering lessons learned (Lessons 10 & 11 added) |

---

## 5. AdminDashboard Not Showing Data — Root Cause (2026-04-06)

### Root Cause

`src/AdminDashboard/appsettings.json` contained:

```json
"OrderApi": { "BaseUrl": "https://localhost:7100;https://localhost:5100" }
```

This value was copied verbatim from the **OrderManagement.API `launchSettings.json`**:

```json
"applicationUrl": "https://localhost:7100;http://localhost:5100"
```

ASP.NET Core's `applicationUrl` uses semicolons to separate multiple profiles (HTTPS + HTTP). That string is **not a valid URI** — `.NET`'s `new Uri("https://localhost:7100;https://localhost:5100")` either throws a `UriFormatException` at startup, or the `HttpClient.BaseAddress` is set to a broken value where every request fails.

`AdminApiService` wraps all calls in `try/catch` and returns empty collections on failure:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed to fetch orders");
    return [];  // ← silently returns empty list, no UI error shown
}
```

This is why the dashboard shows **nothing and no error** — it silently swallows the HTTP failure.

### Fix Applied

**`AdminDashboard/appsettings.json`** — changed to a single valid HTTPS URI:

```json
// Before (broken):
"OrderApi": { "BaseUrl": "https://localhost:7100;https://localhost:5100" }

// After (correct):
"OrderApi": { "BaseUrl": "https://localhost:7100/" }
```

**`CustomerPortal/appsettings.json`** — also corrected (was pointing to wrong port 7050, API runs on 7100):

```json
// Before:
"OrderApi": { "BaseUrl": "https://localhost:7050/" }

// After:
"OrderApi": { "BaseUrl": "https://localhost:7100/" }
```

**`OrderManagement.API/Program.cs`** — CORS origins updated to include the actual ports that CustomerPortal (`60869`) and AdminDashboard (`60873`) run on (from their `launchSettings.json`).

### Verification

After the fix, `AdminApiService` will construct:
```csharp
c.BaseAddress = new Uri("https://localhost:7100/")  // valid URI ✓
```

Requests will go to:
- `https://localhost:7100/api/orders` — returns order list ✓
- `https://localhost:7100/api/admin/stats` — returns revenue/counts ✓

The AdminDashboard will show seeded orders (11 demo orders covering all statuses) and correct revenue (only Completed orders, currently `€323.95` from seed data).
