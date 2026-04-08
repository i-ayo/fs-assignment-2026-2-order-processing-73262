# SportsStore — Change Report

## Summary of All Changes

| # | File | Type | What Changed |
|---|------|------|-------------|
| 1 | `tests/OrderManagement.Tests/OrderManagement.Tests.csproj` | Bug fix | Added InventoryService project reference |
| 2 | `src/AdminDashboard/appsettings.Development.json` | New file | HTTP BaseUrl for dev, avoids untrusted-cert failure |
| 3 | `src/CustomerPortal/appsettings.Development.json` | New file | HTTP BaseUrl for dev, avoids untrusted-cert failure |
| 4 | `src/OrderManagement.API/Program.cs` | Bug fix | Corrected CORS origins with real VS launch ports |
| 5 | `src/AdminDashboard/Components/Pages/Orders.razor` | Feature | FailureReason column + click-to-expand order detail |
| 6 | `src/CustomerPortal/Components/Pages/MyOrders.razor` | Feature | localStorage Customer ID persistence + expandable rows |
| 7 | `src/CustomerPortal/Components/Pages/OrderConfirmation.razor` | UX | Show Customer ID prominently with My Orders hint |
| 8 | `src/OrderManagement.API/Workers/OrderEventConsumer.cs` | Logging | LogContext.PushProperty on all 5 event handlers |
| 9 | `src/OrderManagement.API/Controllers/OrdersController.cs` | Logging | LogContext.PushProperty on Checkout |
| 10 | `src/InventoryService/Workers/InventoryWorker.cs` | Logging | LogContext.PushProperty + structured message templates |
| 11 | `src/PaymentService/Workers/PaymentWorker.cs` | Logging | LogContext.PushProperty + structured message templates |
| 12 | `src/ShippingService/Workers/ShippingWorker.cs` | Logging | LogContext.PushProperty + structured message templates |
| 13 | `tasks/todo.md` | New file | Full task plan |
| 14 | `tasks/lessons.md` | New file | Engineering lessons from this session |
| 15 | `tasks/report.md` | New file | This report |

---

## Bug 1: Build Error (InventoryStoreTests compile failure)

### Root Cause
`tests/OrderManagement.Tests/InventoryStoreTests.cs` contains:
```csharp
using InventoryService.Models;
...
InventoryStore.GetStock(1)
```
The `InventoryService.Models` namespace lives in `src/InventoryService/InventoryService.csproj`.
That project was **not listed** in the test project's `<ItemGroup>` references.
The compiler could not resolve `InventoryStore` → `CS0246 type or namespace not found` → build fails.

### Fix
```xml
<!-- tests/OrderManagement.Tests/OrderManagement.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\src\OrderManagement.API\OrderManagement.API.csproj" />
  <ProjectReference Include="..\..\src\InventoryService\InventoryService.csproj" />  <!-- ADDED -->
  <ProjectReference Include="..\..\src\Shared.Domain\Shared.Domain.csproj" />
</ItemGroup>
```

### Verification
Before: `dotnet build` fails with `CS0246: The type or namespace name 'InventoryService' could not be found`.
After: `dotnet build` succeeds; `dotnet test` runs all 14 tests.

---

## Bug 2: AdminDashboard Not Showing Data

### Root Cause
Both `AdminDashboard` and `CustomerPortal` have this in `appsettings.json`:
```json
"OrderApi": { "BaseUrl": "https://localhost:7100/" }
```
When running locally in Development the `AdminApiService` and `OrderApiService` make server-to-server
`HttpClient` calls to `https://localhost:7100/`.

If `dotnet dev-certs https --trust` has **not** been run (common on fresh machines), the TLS
handshake fails: `HttpRequestException: The SSL connection could not be established`.

The catch blocks in both services silently swallow the exception:
```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed to fetch orders");
    return [];   // ← empty list, no visible error in UI
}
```
Result: the UI shows zero orders, zero revenue — as if the database is empty.

### Fix
Added `appsettings.Development.json` to both frontends pointing to the API's plain-HTTP listener:
```json
{
  "OrderApi": { "BaseUrl": "http://localhost:5100/" }
}
```
The API's `launchSettings.json` already declares `"applicationUrl": "https://localhost:7100;http://localhost:5100"`,
so `http://localhost:5100` is always available in Development with no certificate required.

### Verification
After adding the dev appsettings, the `HttpClient` calls succeed without a trusted certificate.
AdminDashboard shows all 11 seeded orders and correct revenue (£323.95 from the one Completed order).

---

## Bug 3: My Orders — "DeserializeUnableToConvertValue, System.String Path: $[0].status"

### Root Cause (already fixed in codebase, documented here for completeness)
`OrderResponse.Status` is of type `OrderStatus` (enum). Without `JsonStringEnumConverter`:
- API serializes as integer: `"status": 9`
- Client deserializer expects enum, gets JSON string `"Completed"` → `JsonException`

**Before (broken JSON)**:
```json
[{ "id": "...", "status": 9, "totalAmount": 323.95, ... }]
```
**After (correct JSON)**:
```json
[{ "id": "...", "status": "Completed", "totalAmount": 323.95, ... }]
```

**Fixes applied**:
1. `OrderManagement.API/Program.cs` → `AddJsonOptions` registers `JsonStringEnumConverter` globally
2. `CustomerPortal/Services/OrderApiService.cs` → `_opts` includes `JsonStringEnumConverter`
3. `AdminDashboard/Services/AdminApiService.cs` → `_opts` includes `JsonStringEnumConverter`
4. `OrderDbContext` → `HasConversion<string>()` stores status as "Completed" not `9` in DB

---

## Features Added

### AdminDashboard: Order Detail Expansion
Clicking any row in the Orders table expands an inline detail panel showing:
- All order lines (product name, quantity, unit price, line total)
- Shipping address
- Payment transaction ID (if present)
- Tracking number (if present)
- Full failure reason (if failed)
- Last-updated timestamp

The Failure Reason is also shown as a new column in the summary row, truncated with `text-overflow:ellipsis` and a `title` tooltip for the full text.

### My Orders: localStorage Persistence
Customer ID is now saved to `localStorage` under key `sportsstore_customer_id`:
- **On page load**: reads saved ID, pre-fills the input, and auto-loads orders
- **On submit**: writes the ID back to `localStorage` so it persists across browser sessions
- JS interop is done in `OnAfterRenderAsync(firstRender: true)` to avoid prerendering failures

### My Orders + Admin Orders: Expandable Line Detail
Both pages now show a `▸/▾` indicator on each row. Clicking expands the row to reveal order lines, shipping info, payment ref, tracking, and failure reason.

### Order Confirmation: Customer ID Callout
The confirmation page now prominently displays the Customer ID in a monospace badge with the text:
> "Use this to look up your order in My Orders."

This closes the UX gap where users didn't know what Customer ID to type on the My Orders page.

### Serilog Structured Logging
All event handlers across all four services now push to `LogContext`:
```
OrderId=       <guid>
CorrelationId= <guid>
CustomerId=    <string>   (where available)
ServiceName=   OrderManagement.API | InventoryService | PaymentService | ShippingService
EventType=     OrderSubmitted | InventoryConfirmed | InventoryFailed |
               PaymentApproved | PaymentFailed | ShippingCreated
```
These appear as structured fields in Seq, file logs, and any other Serilog sink, enabling
cross-service correlation by filtering `OrderId = X`.

---

## End-to-End Verification Steps

1. Start SQL Server LocalDB (or SQL Server)
2. `dotnet run --project src/OrderManagement.API` → `http://localhost:5100` (schema + seed on startup)
3. `dotnet run --project src/InventoryService` (optional — needs RabbitMQ)
4. `dotnet run --project src/PaymentService`  (optional — needs RabbitMQ)
5. `dotnet run --project src/ShippingService` (optional — needs RabbitMQ)
6. `dotnet run --project src/AdminDashboard` → open http://localhost:60874
7. `dotnet run --project src/CustomerPortal` → open http://localhost:60872
8. In CustomerPortal: add items to cart → checkout → place order
9. Order appears in AdminDashboard → verify status progression if RabbitMQ is running
10. In My Orders: Customer ID is auto-filled from localStorage → orders load immediately
11. `dotnet test` → all tests pass

**Without RabbitMQ**: Orders stay at `InventoryPending` — expected behaviour. The 11 seeded orders
cover all statuses for AdminDashboard demos.

---

## Remaining Risks / Limitations

| Area | Risk | Mitigation |
|------|------|-----------|
| `Guid.NewGuid()` in `SeedOrders` | `OrderLine.Id` is regenerated each startup. `EnsureCreated` only seeds on first run so this is harmless, but would cause issues if switched to EF Migrations. | Replace with deterministic GUIDs before adding migrations. |
| `InventoryStore` is in-memory | Stock is reset every time InventoryService restarts. Orders that reserved stock are forgotten on restart. | Acceptable for demo. Production needs a persistent inventory DB. |
| No authentication | Customer ID is a free-text field. Any user can look up any customer's orders by guessing an ID. | Acceptable per brief. Full auth is explicitly out of scope. |
| `appsettings.Development.json` uses HTTP | If running the API behind HTTPS only, frontends won't connect. | The API already listens on both HTTP and HTTPS in dev, so this is safe. |
| Seq sink referenced in `OrderManagement.API.csproj` | `Serilog.Sinks.Seq` is installed but no `Seq:ServerUrl` is configured. Seq output is silently disabled. | Add `"Serilog": { "WriteTo": [{ "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }] }` to appsettings if a Seq instance is available. |
