# Assessment Preparation Guide
## Distributed Order Processing Platform — Full Stack Assignment 2

> Use this document before your assessment to refresh your understanding of what you built, why each decision was made, and how to explain it confidently to your assessor.

---

## 1. What You Built — The Big Picture

You extended a basic ASP.NET MVC SportsStore shopping cart into a **distributed, event-driven microservices platform**. When a customer places an order, it does not complete synchronously — instead it flows asynchronously through three independent backend services (Inventory → Payment → Shipping), coordinated through a **message broker (RabbitMQ)**.

**The key architectural insight**: the Order Management API never talks directly to the Inventory, Payment, or Shipping services. They communicate only through RabbitMQ messages (events). This is **loose coupling** — each service can fail, restart, or be replaced without breaking the others.

---

## 2. System Components — What Each One Does

### OrderManagement.API
**Role:** The brain and entry point of the system.

When a customer checks out:
1. The Blazor frontend sends a `POST /api/orders/checkout` request
2. The API creates an `Order` record in SQL Server (status: `Submitted`)
3. It immediately publishes an `OrderSubmittedEvent` message to RabbitMQ
4. The order status moves to `InventoryPending` and the API returns `201 Created`
5. A background worker inside the same API (`OrderEventConsumer`) continuously listens on five RabbitMQ exchanges and updates the database as each service completes

The API also exposes:
- `GET /api/orders` — all orders (with optional status/customer filters)
- `GET /api/orders/{id}` — single order
- `GET /api/customers/{id}/orders` — customer's order history
- `GET /api/admin/stats` — revenue and order counts for the dashboard
- `GET /api/inventory/{productId}` — stock level

### InventoryService
**Role:** Checks if items are in stock.

- Runs as a **BackgroundService** (always-on worker process)
- Subscribes to the `order.submitted` RabbitMQ exchange
- When a message arrives: checks `InventoryStore` (thread-safe in-memory dictionary) for each product in the order
- If all items available → **reserves** the stock (decrements quantities atomically) → publishes `InventoryConfirmedEvent`
- If any item is out of stock → publishes `InventoryFailedEvent` with the product name

### PaymentService
**Role:** Simulates payment authorization.

- Subscribes to `inventory.confirmed` exchange
- Approves ~90% of payments
- Randomly rejects ~10% to simulate real payment gateway behaviour
- Publishes `PaymentApprovedEvent` (with a generated transaction ID) or `PaymentFailedEvent`

### ShippingService
**Role:** Creates a shipment record.

- Subscribes to `payment.approved` exchange
- Generates a tracking reference in format `TRK-{YYYYMMDD}-{orderId[..7]}`
- Publishes `ShippingCreatedEvent`

### CustomerPortal (Blazor Server)
**Role:** The customer-facing web application.

Pages:
- `/` — Hero landing page (category cards, feature strip)
- `/shop` — Product catalogue with category pills, search, sort, add-to-cart
- `/cart` — Cart with live quantity update (−/+), remove, line totals
- `/checkout` — Customer ID (CUST001 format), name auto-fill, address, order submission
- `/order-confirmation/{id}` — Order placed confirmation with status and Customer ID reminder
- `/my-orders` — Look up orders by Customer ID with expandable row detail

### AdminDashboard (Blazor Server)
**Role:** The operations/admin interface.

Pages:
- `/` — Dashboard: total orders, revenue (all time / this week / this month), completed count, failed count — auto-refreshes every 10 seconds
- `/orders` — Orders table: filter by status/customer, sortable columns, click any row to expand order lines + shipping + payment details, live 15-second auto-refresh

---

## 3. Key Technologies — What They Are and Why You Used Them

### RabbitMQ (CloudAMQP)
**What it is:** A message broker. Services publish messages to "exchanges"; other services receive them from "queues" bound to those exchanges.

**Why:** Decouples the services. The API doesn't need to know anything about Inventory — it just puts a message on a queue. If InventoryService is down, the message waits until it comes back up. Nothing is lost.

**How you configured it:**
- All exchanges are **fanout** type (broadcast to all bound queues)
- Queues are **durable** (survive broker restart)
- Messages are **persistent** (survive broker restart)
- Connection uses **TLS port 5671** (CloudAMQP hosted, secure)
- Each service declares its own queue bound to the relevant exchange on startup

**If the assessor asks:** "Why fanout instead of direct/topic?" — Fanout means multiple consumers can subscribe to the same event without the publisher knowing who they are. For example, both `OrderEventConsumer` (updates DB) and `PaymentService` (processes payment) both listen to `inventory.confirmed`. With direct routing you'd need to know both recipients at publish time.

---

### Serilog (Structured Logging)
**What it is:** A logging library that writes structured data, not plain text strings.

**Why:** Instead of `"Order 12345 processed"` (unreadable at scale), you get searchable structured fields: `OrderId=12345, CustomerId=CUST001, EventType=InventoryConfirmed`.

**What you configured:**

Three sinks (output destinations) in every service:
1. **Console** — clean human-readable output during development: `HH:mm:ss [INF] Message`
2. **Rolling File** — writes to `logs/<service>-YYYYMMDD.log`, kept for 7 days, full structured template with `{Properties}`
3. **Seq** — a log aggregation server (run locally at `http://localhost:5341`). Receives all logs with all properties, lets you search/filter across all services in one UI

Four enrichers on every service:
- `.Enrich.FromLogContext()` — picks up properties pushed via `LogContext.PushProperty()`
- `.Enrich.WithMachineName()` — adds the computer name to every log entry
- `.Enrich.WithEnvironmentName()` — adds `Development` or `Production`
- `.Enrich.WithThreadId()` — useful for debugging async/concurrent code

Contextual properties pushed per event (in `OrderEventConsumer`, `InventoryWorker`, `PaymentWorker`, `ShippingWorker`):
```csharp
using var _ = LogContext.PushProperty("OrderId",       evt.OrderId);
using var _ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
using var _ = LogContext.PushProperty("CustomerId",    evt.CustomerId);
using var _ = LogContext.PushProperty("ServiceName",   "InventoryService");
using var _ = LogContext.PushProperty("EventType",     "OrderSubmitted");
```
These appear on every log line written inside that `using` scope — so every message, warning, and error automatically includes the order context without re-passing it.

**If the assessor asks:** "What is structured logging?" — Plain text logs are like sticky notes — you can read one but you can't efficiently search millions of them. Structured logging gives each property a name and value, so you can query "show me all logs where OrderId = X" or "show me all PaymentFailed events this week" — exactly like querying a database.

**How to show Seq live:**
```
docker run -e ACCEPT_EULA=Y -p 5341:80 datalust/seq
```
Then open `http://localhost:5341` and place an order — watch all 5 services' logs stream in with full context.

---

### CQRS with MediatR
**What it is:** CQRS = Command Query Responsibility Segregation. MediatR is the .NET library that implements the mediator pattern — a single `IMediator` bus that routes messages to handlers.

**Why:** Keeps controllers thin and dumb. The controller's only job is: receive HTTP request → send to mediator → return HTTP response. All business logic lives in handlers.

**Commands** (write operations — change state):
| Command | What it does |
|---------|-------------|
| `SubmitOrderCommand` | Creates order in DB, publishes RabbitMQ event |
| `InventoryConfirmedCommand` | Transitions order: `InventoryPending` → `InventoryConfirmed` → `PaymentPending` |
| `InventoryFailedCommand` | Transitions: `InventoryPending` → `InventoryFailed` |
| `PaymentApprovedCommand` | Transitions: `PaymentPending` → `PaymentApproved` → `ShippingPending`, stores transaction ID |
| `PaymentFailedCommand` | Transitions: `PaymentPending` → `PaymentFailed` |
| `ShippingCreatedCommand` | Transitions: `ShippingPending` → `ShippingCreated` → `Completed`, stores tracking number |

**Queries** (read operations — no side effects):
| Query | What it returns |
|-------|----------------|
| `GetOrderByIdQuery` | Single `OrderResponse` |
| `GetAllOrdersQuery` | List of orders, optional status/customer filter |
| `GetCustomerOrdersQuery` | All orders for a specific customer |
| `GetDashboardSummaryQuery` | Revenue stats for admin dashboard |

**Controller example:**
```csharp
[HttpPost("checkout")]
public async Task<IActionResult> Checkout([FromBody] SubmitOrderRequest request)
{
    var order = await mediator.Send(new SubmitOrderCommand(request));
    return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
}
```
That's the entire controller method. Three lines. All logic is in `SubmitOrderCommandHandler`.

**If the assessor asks:** "What is the benefit of CQRS?" — It separates read and write concerns. Reads can be optimised for queries (projections, caching, read replicas) without affecting write logic. It also makes the system more testable — you can test a handler in isolation with an in-memory database without spinning up the whole API.

---

### AutoMapper
**What it is:** A library that automatically copies values between objects that have matching property names.

**Why:** Without AutoMapper, every controller and handler would have code like:
```csharp
var response = new OrderResponse {
    Id = order.Id,
    CustomerId = order.CustomerId,
    // ... 12 more properties
    Lines = order.Lines.Select(l => new OrderLineResponse { ... }).ToList()
};
```
AutoMapper replaces all of that with `mapper.Map<OrderResponse>(order)`.

**Your mapping profile (`OrderMappingProfile.cs`):**
```csharp
CreateMap<Order, OrderResponse>();
CreateMap<OrderLine, OrderLineResponse>()
    .ForMember(d => d.LineTotal, o => o.MapFrom(s => s.Quantity * s.UnitPrice));
```
`LineTotal` is computed (not stored in DB) — AutoMapper calls the lambda to calculate it at mapping time.

**If the assessor asks:** "Where is AutoMapper used?" — In every query handler: `GetAllOrdersQueryHandler`, `GetOrderByIdQueryHandler`, `GetCustomerOrdersQueryHandler`. When a query returns orders from the database, the handler maps `Order` entity → `OrderResponse` DTO before returning it to the controller.

---

### Entity Framework Core + SQL Server
**What it is:** An ORM (Object-Relational Mapper) — maps C# classes to database tables.

**Database:** SQL Server LocalDB (`(localdb)\MSSQLLocalDB`, database: `OrderProcessing`)

**How schema is created:** `db.Database.EnsureCreated()` on API startup — creates tables from entity classes if they don't exist. No migration files needed for this project.

**Seed data:** 11 orders are seeded via `HasData` in `OnModelCreating`, covering every possible `OrderStatus` so the admin dashboard has data to display from the moment the app starts.

**Order state machine** — the `OrderStatus` enum:
```
Submitted → InventoryPending → InventoryConfirmed → PaymentPending → PaymentApproved → ShippingPending → ShippingCreated → Completed
                            ↘ InventoryFailed                    ↘ PaymentFailed
```

---

### Blazor Server
**What it is:** Microsoft's C# web UI framework. Instead of running JavaScript in the browser, Blazor Server runs components on the server and syncs the DOM over a **SignalR WebSocket** connection.

**Key implementation detail — why buttons were not working and how it was fixed:**

Blazor has two render modes:
- `prerender: true` (default) — server renders static HTML first, then the SignalR circuit connects and "hydrates" the page. Until the circuit connects, buttons are visually present but completely non-interactive.
- `prerender: false` — page only renders after the circuit is live. No dead period.

The fix applied to both `CustomerPortal/App.razor` and `AdminDashboard/App.razor`:
```razor
<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />
```

**CartService reactivity pattern** — how the cart badge and cart page stay in sync:
```csharp
public event Action? StateChanged;  // fire after every mutation
```
Every component that displays cart data subscribes:
```csharp
protected override void OnInitialized()
    => CartSvc.StateChanged += OnCartChanged;

private async void OnCartChanged()
    => await InvokeAsync(StateHasChanged);  // marshals back to Blazor's sync context

public void Dispose()
    => CartSvc.StateChanged -= OnCartChanged;
```
`InvokeAsync(StateHasChanged)` is required because the `StateChanged` event fires from a different thread — `InvokeAsync` marshals the call back onto the Blazor dispatcher.

---

### Docker
**What it is:** Containerisation — each service runs in an isolated environment with everything it needs.

**Your setup:**
- 6 multi-stage `Dockerfile`s (one per service)
- `docker-compose.yml` orchestrates: SQL Server + RabbitMQ (required) + all 6 app services

```bash
docker compose up --build
```
Brings up the entire platform on:
- Customer Portal: http://localhost:5100
- Admin Dashboard: http://localhost:5200
- API: http://localhost:5050
- RabbitMQ Management: http://localhost:15672

Multi-stage Dockerfile pattern (build in SDK image, run in smaller aspnet runtime):
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build    # ~900MB — only for building
# ... dotnet publish ...
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final # ~200MB — for running
COPY --from=build /app/publish .
```
This keeps the final image small — the SDK tools are not shipped in production.

---

### GitHub Actions CI/CD

**CI workflow (`ci.yml`)** — runs on every push/PR to master:
1. Restore → Build → Test (with XPlat Code Coverage)
2. Generates HTML coverage report using `reportgenerator`
3. Uploads test results (`.trx`) and coverage report as artifacts
4. On push to master: deploys HTML coverage report to **GitHub Pages**

**CD workflow (`deploy.yml`)** — runs only when CI passes on master:
1. Builds all 6 Docker images
2. Pushes to **GitHub Container Registry** (`ghcr.io`) with short SHA tag + `latest`

**If the assessor asks:** "Why separate CI and CD?" — CI (Continuous Integration) is about quality gates — it should always run, even on PRs. CD (Continuous Deployment) should only happen after CI passes and only on the main branch. You don't want to push broken images to your registry.

---

## 4. Order Lifecycle — Walkthrough for Demo

Here is exactly what happens when you place an order:

1. **Customer Portal** — add items to cart → checkout → enter `CUST001` → submit
2. **POST /api/orders/checkout** hits `OrdersController` → `SubmitOrderCommand` → `SubmitOrderCommandHandler`:
   - Creates `Order` entity in SQL Server (status: `Submitted`)
   - Calls `_publisher.PublishAsync("order.submitted", event)` → RabbitMQ
   - Status immediately set to `InventoryPending`, returned as `201 Created`
3. **InventoryService** receives message from `inventory.order.submitted` queue:
   - Checks stock for each product, decrements if available
   - Publishes `InventoryConfirmedEvent` to `inventory.confirmed` exchange
4. **OrderEventConsumer** (in OrderManagement.API) receives `InventoryConfirmedEvent`:
   - Sends `InventoryConfirmedCommand` via MediatR → handler updates DB: `InventoryConfirmed` → `PaymentPending`
5. **PaymentService** also receives from `inventory.confirmed` (separate queue):
   - Simulates payment, publishes `PaymentApprovedEvent`
6. **OrderEventConsumer** receives `PaymentApprovedEvent`:
   - Sends `PaymentApprovedCommand` → DB: `PaymentApproved` → `ShippingPending`
7. **ShippingService** receives from `payment.approved`:
   - Generates tracking number, publishes `ShippingCreatedEvent`
8. **OrderEventConsumer** receives `ShippingCreatedEvent`:
   - Sends `ShippingCreatedCommand` → DB: `ShippingCreated` → **`Completed`**

Total time from submit to completed: **~3–5 seconds** in normal operation.

---

## 5. Seq — How to Show It

**Start Seq:**
```bash
docker run --name seq -e ACCEPT_EULA=Y -p 5341:80 datalust/seq
```
Open: http://localhost:5341

**What to show the assessor:**
1. Start all 5 services locally
2. Place an order from the Customer Portal
3. In Seq: search `OrderId = "<the order GUID>"`
4. You'll see every log entry across all 5 services for that single order — timestamped, with `ServiceName`, `EventType`, `CorrelationId`, `CustomerId` all visible as searchable columns
5. This demonstrates the full distributed trace of one order through the system

**Key Seq queries to know:**
```
# All logs for one order
OrderId = "3fa85f64-..."

# All inventory confirmations
EventType = "InventoryConfirmed"

# All errors across all services
@Level = "Error"

# All payment failures
EventType = "PaymentFailed"

# Logs from one service
Application = "PaymentService"
```

---

## 6. Common Assessor Questions — Model Answers

**Q: What is event-driven architecture?**
A: "Services communicate by publishing and consuming events rather than calling each other directly. The Order API doesn't know Inventory exists — it just puts an `OrderSubmitted` event on a queue. Inventory independently reads from that queue. This means services are decoupled — they can be deployed, scaled, or updated independently."

**Q: What is the difference between a command and a query in CQRS?**
A: "A command is an instruction to change state — like `SubmitOrderCommand`. It has a single handler, may have side effects, and returns a result indicating success or failure. A query is a read-only request — like `GetOrderByIdQuery`. It returns data, has no side effects, and never changes the system. Separating them keeps the codebase clean and makes optimisation easier."

**Q: What is Blazor and how is it different from React?**
A: "Blazor Server runs all UI logic in C# on the server. The browser only runs a thin JavaScript shim that syncs DOM changes over a SignalR WebSocket. React runs JavaScript in the browser and calls APIs. Blazor means the entire full-stack can be C# — no context switching between languages."

**Q: Why did you use RabbitMQ over direct HTTP calls between services?**
A: "HTTP is synchronous and tightly coupled. If Payment Service is down when an order is placed, the checkout fails. With RabbitMQ, the message waits in the queue until Payment Service comes back up — the order eventually completes without any data loss. It also allows horizontal scaling: you can run 5 instances of InventoryService all reading from the same queue."

**Q: What does AutoMapper do and where did you use it?**
A: "AutoMapper eliminates repetitive property-by-property mapping code. I used it in the query handlers to convert `Order` database entities to `OrderResponse` DTOs before returning them to the API clients. The mapping profile also computes `LineTotal = Quantity × UnitPrice` at mapping time since that's not stored in the database."

**Q: How does structured logging differ from Console.WriteLine?**
A: "`Console.WriteLine($"Order {id} failed")` produces a string — you can't query it. `logger.LogError("Order {OrderId} failed", id)` with Serilog stores `OrderId` as a searchable property. In Seq you can then query `OrderId = X` or `@Level = Error` and get all matching events instantly, even across millions of log entries."

**Q: What is the purpose of a CorrelationId?**
A: "A single business action (placing one order) spans 5 different services. Each service logs with the same `CorrelationId` — a GUID created when the order is submitted. This lets you reconstruct the complete trace of one order across all service logs, which is essential for debugging in a distributed system."

---

## 7. Quick Reference — Ports When Running Locally

| Service | URL |
|---------|-----|
| Customer Portal | https://localhost:60869 |
| Admin Dashboard | https://localhost:60873 |
| Order Management API | https://localhost:7100 |
| InventoryService | (background worker — no UI) |
| PaymentService | (background worker — no UI) |
| ShippingService | (background worker — no UI) |
| Seq (if running) | http://localhost:5341 |

| Docker Compose | URL |
|----------------|-----|
| Customer Portal | http://localhost:5100 |
| Admin Dashboard | http://localhost:5200 |
| Order API | http://localhost:5050 |
| RabbitMQ Management | http://localhost:15672 |

---

## 8. Files Worth Knowing for the Assessment

| File | Why it matters |
|------|---------------|
| `OrderManagement.API/Program.cs` | DI registration, Serilog config, EF setup, RabbitMQ publisher singleton |
| `OrderManagement.API/CQRS/` | All commands, queries, handlers — the core CQRS implementation |
| `OrderManagement.API/Workers/OrderEventConsumer.cs` | The bridge between RabbitMQ and the database — listens to all 5 result exchanges |
| `OrderManagement.API/Profiles/OrderMappingProfile.cs` | AutoMapper configuration |
| `OrderManagement.API/Data/OrderDbContext.cs` | EF Core schema + seed data |
| `InventoryService/Workers/InventoryWorker.cs` | Full message consumer lifecycle |
| `CustomerPortal/Services/CartService.cs` | `StateChanged` event pattern |
| `CustomerPortal/Components/App.razor` | `prerender: false` fix — critical |
| `.github/workflows/ci.yml` | Build → Test → Coverage → GitHub Pages |
| `.github/workflows/deploy.yml` | Docker Build → Push to GHCR |
| `docker-compose.yml` | Full stack local orchestration |
