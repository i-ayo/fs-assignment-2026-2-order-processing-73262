# SportsStore — Task Plan

## Status Key
- [x] Done
- [-] In progress
- [ ] Not started

---

## A. Build Error Fix
- [x] Add `InventoryService` project reference to `tests/OrderManagement.Tests/OrderManagement.Tests.csproj`
  - **Root cause**: `InventoryStoreTests.cs` imports `InventoryService.Models.InventoryStore` but the csproj had no matching `<ProjectReference>`.
  - **Fix**: Added `<ProjectReference Include="..\..\src\InventoryService\InventoryService.csproj" />`

---

## B. AdminDashboard Not Showing Data
- [x] Create `src/AdminDashboard/appsettings.Development.json`
  - **Root cause**: Both Blazor frontends call `https://localhost:7100/` server-to-server. If `dotnet dev-certs https --trust` has not been run, every `HttpClient` request fails with an SSL exception that is silently caught and returns empty data.
  - **Fix**: Development override uses `http://localhost:5100/` (the API's plain-HTTP listener) so no certificate trust is needed in dev.
- [x] Create `src/CustomerPortal/appsettings.Development.json` (same reason)

---

## C. CORS Origins
- [x] Fix CORS origins in `OrderManagement.API/Program.cs` to include actual VS launch ports (60869, 60872, 60873, 60874) with correct comments.

---

## D. AdminDashboard — Orders Page
- [x] Add **Failure Reason** column (shows `⚠ reason` for failed orders, `—` otherwise)
- [x] Add **click-to-expand** row detail panel showing:
  - Order lines (product, qty, price, total)
  - Shipping address
  - Payment transaction ID
  - Tracking number
  - Failure reason (repeated in full, not truncated)
  - Last-updated timestamp
- [x] Revenue in table footer counts completed orders only

---

## E. My Orders UX
- [x] Persist Customer ID in `localStorage` via `IJSRuntime`
  - On first render: read `sportsstore_customer_id` from localStorage, pre-fill input, auto-load orders
  - On load: write entered ID back to localStorage
- [x] Add expandable order row detail (lines, tracking, payment ref, failure reason)
- [x] Friendly empty-state message explains that the Customer ID must match checkout

---

## F. Order Confirmation
- [x] Show Customer ID prominently on the confirmation page with a hint "Use this to look up your order in My Orders"

---

## G. Serilog Structured Logging
- [x] Push `OrderId`, `CorrelationId`, `CustomerId`, `EventType`, `ServiceName` into `LogContext` for:
  - `OrderEventConsumer` (all 5 handlers)
  - `InventoryWorker`
  - `PaymentWorker`
  - `ShippingWorker`
  - `OrdersController.Checkout`

---

## H. Tests
- [x] Existing test suite covers:
  - Enum serialization roundtrip (`OrderStatusSerializationTests`)
  - All order status transitions (`OrdersControllerTests`)
  - InventoryStore reserve / out-of-stock (`InventoryStoreTests`)
- [x] Build error that prevented tests from running is now fixed

---

## Pending / Out of Scope
- [ ] Full authentication / login system (explicitly out of scope per brief)
- [ ] Seq sink configuration (package referenced, not configured — safe to add `Seq:ServerUrl` to appsettings if a Seq instance is running)
- [ ] Docker Dockerfiles (referenced in compose but not present — out of scope for this session)
