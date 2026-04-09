# SportsStore — Distributed Order Processing System

**Full Stack Development · Semester 2 · Assignment 2**

A production-grade, event-driven microservices application built with **.NET 10**, **Blazor Server**, **RabbitMQ / CloudAMQP**, and **Entity Framework Core**. Orders placed on the Customer Portal travel through an asynchronous pipeline of worker services — Inventory, Payment, and Shipping — each publishing and consuming domain events over a cloud-hosted AMQPS broker, with full status visibility in the Admin Dashboard.

---

## Table of Contents

1. [System Architecture](#system-architecture)
2. [Services](#services)
3. [Event Flow](#event-flow)
4. [Technology Stack](#technology-stack)
5. [Prerequisites](#prerequisites)
6. [Configuration & Credentials](#configuration--credentials)
7. [Running the Application](#running-the-application)
8. [Seed Data](#seed-data)
9. [Structured Logging & Observability](#structured-logging--observability)
10. [Customer ID Format](#customer-id-format)
11. [Design Decisions & Assumptions](#design-decisions--assumptions)
12. [Known Limitations](#known-limitations)
13. [Project Structure](#project-structure)

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        CLIENT LAYER (Blazor Server)                     │
│                                                                         │
│   ┌──────────────────────┐          ┌──────────────────────┐            │
│   │   CustomerPortal     │          │    AdminDashboard     │            │
│   │   :5000 / :5100      │          │    :5001 / :5101      │            │
│   │  Products, Checkout  │          │  Live order monitor   │            │
│   │  My Orders, Cart     │          │  Status filter/sort   │            │
│   └──────────┬───────────┘          └──────────┬───────────┘            │
│              │  HTTP (JSON)                     │  HTTP (JSON)           │
└──────────────┼──────────────────────────────────┼───────────────────────┘
               │                                  │
               ▼                                  ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                     OrderManagement.API  :5100 / :7100                  │
│  ASP.NET Core Web API · EF Core · SQL Server LocalDB                    │
│  POST /api/orders  ·  GET /api/orders/{id}  ·  GET /api/orders?cid=…    │
│  GET /api/inventory/check  ·  Swagger UI                                │
│                                                                          │
│  ┌─────────────────────┐    ┌──────────────────────────┐                │
│  │  RabbitMqPublisher  │    │   OrderEventConsumer     │                │
│  │  (publishes events) │    │   (updates order status) │                │
│  └──────────┬──────────┘    └───────────┬──────────────┘                │
└─────────────┼─────────────────────────── ┼────────────────────────────────┘
              │  AMQPS TLS · Port 5671      │
              ▼                             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│               CloudAMQP / LavinMQ  (ostrich.lmq.cloudamqp.com)          │
│                                                                          │
│   Exchanges (fanout, durable):                                           │
│     order.submitted · inventory.confirmed · inventory.failed             │
│     payment.approved · payment.failed · shipping.created                 │
└───┬─────────────────────────┬───────────────────────┬───────────────────┘
    │                         │                       │
    ▼                         ▼                       ▼
┌──────────────┐   ┌──────────────────┐   ┌──────────────────┐
│InventoryService│  │  PaymentService  │   │  ShippingService │
│  Worker SDK  │   │   Worker SDK     │   │   Worker SDK     │
│ Checks stock │   │ 90% approve /    │   │ Assigns tracking │
│ confirms or  │   │ 10% random fail  │   │ marks Completed  │
│ fails order  │   │                  │   │                  │
└──────────────┘   └──────────────────┘   └──────────────────┘
```

All services connect to CloudAMQP using **AMQPS (TLS)** on port 5671 with a shared virtual host and credential set managed via .NET User Secrets.

---

## Services

| Service | SDK | Port(s) | Role |
|---|---|---|---|
| `OrderManagement.API` | `Microsoft.NET.Sdk.Web` | 5100 (HTTP) / 7100 (HTTPS) | REST API gateway; persists orders to SQL Server; publishes `OrderSubmittedEvent`; consumes all downstream events to update order status |
| `InventoryService` | `Microsoft.NET.Sdk.Worker` | — | Subscribes to `order.submitted`; checks stock; publishes `InventoryConfirmedEvent` or `InventoryFailedEvent` |
| `PaymentService` | `Microsoft.NET.Sdk.Worker` | — | Subscribes to `inventory.confirmed`; simulates payment (90 % approve / 10 % fail); publishes `PaymentApprovedEvent` or `PaymentFailedEvent` |
| `ShippingService` | `Microsoft.NET.Sdk.Worker` | — | Subscribes to `payment.approved`; assigns a tracking number; publishes `ShippingCreatedEvent`; `OrderEventConsumer` marks the order **Completed** |
| `CustomerPortal` | `Microsoft.NET.Sdk.Web` | 5000 (HTTP) / 5001 (HTTPS) | Blazor Server UI for customers: product catalogue, cart, checkout, My Orders |
| `AdminDashboard` | `Microsoft.NET.Sdk.Web` | 5002 (HTTP) / 5003 (HTTPS) | Blazor Server UI for operators: live order table with status filtering and sorting |
| `Shared.Domain` | class library | — | Shared enums (`OrderStatus`), domain events, and DTOs used across all projects |

---

## Event Flow

Orders progress through a strict state machine driven entirely by asynchronous domain events:

```
Customer places order
        │
        ▼
  [POST /api/orders]
  Status: Submitted
        │
        │  publishes ──► order.submitted (fanout exchange)
        │
        ▼
  InventoryService
  Checks stock levels
        ├── enough stock ──► Status: InventoryConfirmed
        │                    publishes ──► inventory.confirmed
        │                         │
        │                         ▼
        │                   PaymentService
        │                   Simulates payment gateway
        │                         ├── approved (90%) ──► Status: PaymentApproved
        │                         │                      publishes ──► payment.approved
        │                         │                           │
        │                         │                           ▼
        │                         │                     ShippingService
        │                         │                     Assigns tracking #
        │                         │                           │
        │                         │                           ▼
        │                         │                     Status: Completed ✅
        │                         │
        │                         └── failed (10%) ──► Status: PaymentFailed ❌
        │
        └── out of stock ──► Status: InventoryFailed ❌
```

**Exchange / Queue Map**

| Exchange | Type | Bound Queue | Consumer |
|---|---|---|---|
| `order.submitted` | fanout | `inventory.order.submitted` | InventoryWorker |
| `inventory.confirmed` | fanout | `payment.inventory.confirmed` | PaymentWorker |
| `inventory.confirmed` | fanout | `api.inventory.confirmed` | OrderEventConsumer |
| `inventory.failed` | fanout | `api.inventory.failed` | OrderEventConsumer |
| `payment.approved` | fanout | `shipping.payment.approved` | ShippingWorker |
| `payment.approved` | fanout | `api.payment.approved` | OrderEventConsumer |
| `payment.failed` | fanout | `api.payment.failed` | OrderEventConsumer |
| `shipping.created` | fanout | `api.shipping.created` | OrderEventConsumer |

All exchanges and queues are **durable**. Fanout topology allows multiple consumers to bind to the same exchange independently, enabling both the domain worker and the API's status-update consumer to receive the same event.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Web API | ASP.NET Core Minimal API / Controller-based |
| Frontend | Blazor Server (Interactive Server render mode) |
| Messaging | RabbitMQ.Client 7.x over AMQPS (TLS port 5671) |
| Broker | CloudAMQP (LavinMQ) — hosted RabbitMQ-compatible |
| ORM | Entity Framework Core 10 with SQL Server provider |
| Database | SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) |
| Logging | Serilog with Console, Rolling File, and Seq sinks |
| Credential management | .NET User Secrets (`dotnet user-secrets`) |
| UI state | Blazor Server circuit + browser `localStorage` (JS interop) |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server LocalDB (included with Visual Studio, or install [SQL Server Express LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb))
- A CloudAMQP account and instance — the AMQPS URL and credentials are required (see [Configuration](#configuration--credentials))
- *(Optional)* [Seq](https://datalust.co/seq) running locally on `http://localhost:5341` for structured log exploration

---

## Configuration & Credentials

RabbitMQ credentials are **never committed to source control**. Every `appsettings.json` contains the placeholder value `"Secrets"` for `RabbitMQ:Password`. The real password must be injected at runtime using one of the following methods:

### Option A — .NET User Secrets (recommended for development)

Run this once per service from within its project directory:

```bash
# OrderManagement.API
cd OrderManagement.API
dotnet user-secrets set "RabbitMQ:Password" "your-real-password"

# InventoryService
cd ../InventoryService
dotnet user-secrets set "RabbitMQ:Password" "your-real-password"

# PaymentService
cd ../PaymentService
dotnet user-secrets set "RabbitMQ:Password" "your-real-password"

# ShippingService
cd ../ShippingService
dotnet user-secrets set "RabbitMQ:Password" "your-real-password"
```

Each project has a unique `UserSecretsId` in its `.csproj`, so secrets are stored separately in the OS user profile and never appear in the repository.

### Option B — Environment Variable

Set the environment variable before launching any service:

```bash
# PowerShell
$env:RabbitMQ__Password = "your-real-password"

# Bash / WSL
export RabbitMQ__Password="your-real-password"
```

The double underscore `__` maps to the `:` separator in the .NET configuration hierarchy.

### Option C — Edit appsettings.json directly (not recommended for shared repos)

Replace `"Secrets"` with the real password in each `appsettings.json`. **Do not commit this change.**

### Complete RabbitMQ configuration reference

```json
"RabbitMQ": {
  "Host":        "ostrich.lmq.cloudamqp.com",
  "Port":        5671,
  "Username":    "evilufrt",
  "Password":    "your-real-password-here",
  "VirtualHost": "evilufrt",
  "UseTls":      true
}
```

---

## Running the Application

All six processes must run simultaneously. Open six terminal windows (or use the Visual Studio / Rider multi-startup configuration) and start each from the repository root:

```bash
# Terminal 1 — REST API (must start first; creates the database on boot)
dotnet run --project OrderManagement.API

# Terminal 2 — Inventory worker
dotnet run --project InventoryService

# Terminal 3 — Payment worker
dotnet run --project PaymentService

# Terminal 4 — Shipping worker
dotnet run --project ShippingService

# Terminal 5 — Customer Portal
dotnet run --project CustomerPortal

# Terminal 6 — Admin Dashboard
dotnet run --project AdminDashboard
```

Once running, the API logs the HTTP and HTTPS URLs it is listening on. The portals log their own bound URLs via `ApplicationStarted` lifecycle hook.

**Default URLs (from launchSettings.json)**

| Application | URL |
|---|---|
| OrderManagement.API | http://localhost:5100 |
| OrderManagement.API (Swagger) | http://localhost:5100/swagger |
| CustomerPortal | http://localhost:5000 |
| AdminDashboard | http://localhost:5002 |

> The API uses `EnsureCreated()` on startup — no migrations are needed. The database is created automatically with seed data on first run.

---

## Seed Data

The database is pre-seeded with **11 demo orders** covering every possible `OrderStatus` value, so the Admin Dashboard renders a complete, varied table immediately on first launch.

**Seeded Customers**

| Customer ID | Name | Default Address |
|---|---|---|
| CUST001 | Alice Martin | 1 Main St, Dublin, D01 AB12 |
| CUST002 | Bob Smith | 2 River Rd, Cork, T12 CD34 |
| CUST003 | Carol White | 3 Lake Ave, Galway, H91 EF56 |
| CUST004 | Dan Jones | 4 Hill St, Limerick, V94 GH78 |
| CUST005 | Eve Brown | 5 Oak Rd, Waterford, X91 IJ90 |

**Seeded Products** (available in the CustomerPortal catalogue)

Kayak, Lifejacket, Soccer Ball, Corner Flags, Stadium, Thinking Cap, Unsteady Chair, Human Chess Board, Bling-Bling King

All seed records use **fixed GUIDs** so EF Core `HasData` is fully idempotent — re-running the application never produces duplicate rows.

---

## Structured Logging & Observability

Every service uses **Serilog** with three sinks configured in `appsettings.json`:

| Sink | Detail |
|---|---|
| **Console** | Human-readable output in the terminal |
| **Rolling File** | Daily log files under `logs/` in each service directory; 7-day retention |
| **Seq** | Structured log server at `http://localhost:5341`; install [Seq](https://datalust.co/seq) for rich querying |

**Enrichers applied to every log event:** `FromLogContext`, `WithMachineName`, `WithEnvironmentName`, `WithThreadId`

**Key structured properties** emitted by the messaging layer:

| Property | Example value | Where |
|---|---|---|
| `OrderId` | `a1b2c3d4-…` | All worker handlers |
| `CustomerId` | `CUST001` | Workers + API |
| `CorrelationId` | `c0001000-…` | End-to-end trace |
| `EventType` | `OrderSubmitted` | All event handlers |
| `ServiceName` | `InventoryService` | All worker handlers |

These properties enable cross-service correlation queries in Seq such as:

```
EventType = 'PaymentFailed' and OrderId = 'a1b2…'
```

**Log level defaults**

| Environment | Default level | Microsoft / System |
|---|---|---|
| Production | `Information` | `Warning` |
| Development | `Debug` | `Information` |

---

## Customer ID Format

The Customer Portal enforces that Customer IDs follow the format **`CUST` followed by exactly 3 digits** (e.g. `CUST001`, `CUST042`). The format is validated client-side with a regex (`^CUST\d{3}$`) on both the Checkout and My Orders pages:

- Input is normalised to uppercase automatically.
- The field accepts a maximum of 7 characters.
- A red inline error is shown if the format is invalid and action buttons are disabled until corrected.

For the five pre-seeded customers (`CUST001`–`CUST005`), the Full Name field auto-fills and is locked when a known ID is entered — the customer cannot accidentally submit the wrong name. The Shipping Address field remains freely editable; multiple customer IDs can share an address.

---

## Design Decisions & Assumptions

**Fanout exchanges over direct/topic routing** — Using fanout exchanges allows both the downstream worker (e.g., `PaymentService`) and the `OrderEventConsumer` inside the API to consume the same event independently. Adding a new consumer in future requires no change to the publisher or exchange definition.

**Fail-fast connection factory** — Each service's `RabbitMqConnectionFactory` throws `InvalidOperationException` immediately if any required credential is missing or still set to the placeholder `"Secrets"`. Silent no-op fallbacks that hide configuration errors were removed; a service that cannot connect to the broker crashes loudly on startup so the problem is visible in logs.

**EF Core `EnsureCreated` vs migrations** — `EnsureCreated()` is used for simplicity in a demo context. A production deployment would use `dotnet ef migrations` and `Database.Migrate()` instead.

**Payment simulation** — `PaymentService` approves 90 % of orders and randomly fails 10 %. The random seed is not fixed, so each run produces a different mix of approved and failed orders. This demonstrates the full failure path without requiring integration with a real payment gateway.

**Inventory stock levels** — Stock values are held in an in-memory dictionary inside `InventoryService` (not in the database). The worker confirms all orders where the requested quantity does not exceed the available stock. The CustomerPortal calls `GET /api/inventory/check` before checkout to surface stock problems before the order is submitted.

**Blazor circuit resilience** — All `OnInitializedAsync` handlers in the CustomerPortal are wrapped in `try/catch/finally` blocks. If the API is unreachable (e.g., started before the API is ready), the component degrades gracefully rather than breaking the Blazor circuit.

**Toast notifications** — Add-to-cart feedback uses a CSS-animated fixed-position toast (top-right, z-index 9999) that auto-dismisses after 2.5 seconds. Rapid consecutive additions cancel the previous dismiss timer via `CancellationTokenSource`.

**ASPNETCORE_ENVIRONMENT = Production** — All services are configured to run in the Production environment by default (via `launchSettings.json` for web projects and `UseEnvironment("Production")` in `Program.cs` for worker projects). This ensures the Production `appsettings.json` is the authoritative configuration and Development overrides are never applied unintentionally.

---

## Known Limitations

- **No authentication or authorisation** — The API and both portals are unauthenticated. Any browser can access any customer's orders if they know the Customer ID.
- **In-memory inventory** — Stock levels reset on every restart of `InventoryService`. There is no persistent inventory database.
- **LocalDB only** — The `ConnectionStrings:OrdersDb` is configured for SQL Server LocalDB. Switching to a full SQL Server or PostgreSQL instance requires updating the connection string and provider package.
- **No Docker Compose file** — Services are started individually. A `docker-compose.yml` for containerised deployment is not included in this submission.
- **Seq is optional** — If Seq is not running, the Seq sink emits a warning on startup but does not prevent the service from running.
- **Single-instance workers** — There is no competing-consumer or horizontal-scaling setup. Running multiple instances of the same worker would result in duplicate message processing.

---

## Project Structure

```
OrderProcessing/
└── src/
    ├── Shared.Domain/                  # Class library: OrderStatus enum, domain events, DTOs
    │
    ├── OrderManagement.API/            # ASP.NET Core Web API
    │   ├── Controllers/                # OrdersController, InventoryController
    │   ├── Data/                       # OrderDbContext, EF entities, seed data
    │   ├── Messaging/
    │   │   ├── RabbitMqConnectionFactory.cs   # TLS connection builder
    │   │   ├── RabbitMqPublisher.cs           # Publishes OrderSubmittedEvent
    │   │   └── InMemoryPublisher.cs           # Stub (no longer used in production)
    │   ├── Workers/
    │   │   └── OrderEventConsumer.cs          # Background service; updates order status
    │   ├── appsettings.json
    │   └── Program.cs
    │
    ├── InventoryService/               # .NET Worker Service
    │   ├── Messaging/
    │   │   └── RabbitMqConnectionFactory.cs
    │   ├── Workers/
    │   │   └── InventoryWorker.cs
    │   ├── appsettings.json
    │   └── Program.cs
    │
    ├── PaymentService/                 # .NET Worker Service
    │   ├── Messaging/
    │   │   └── RabbitMqConnectionFactory.cs
    │   ├── Workers/
    │   │   └── PaymentWorker.cs
    │   ├── appsettings.json
    │   └── Program.cs
    │
    ├── ShippingService/                # .NET Worker Service
    │   ├── Messaging/
    │   │   └── RabbitMqConnectionFactory.cs
    │   ├── Workers/
    │   │   └── ShippingWorker.cs
    │   ├── appsettings.json
    │   └── Program.cs
    │
    ├── CustomerPortal/                 # Blazor Server — customer-facing
    │   ├── Components/Pages/
    │   │   ├── Products.razor          # Hero banner, catalogue, add-to-cart toast
    │   │   ├── Cart.razor
    │   │   ├── Checkout.razor          # CUST### validation, auto-fill, inventory check
    │   │   ├── OrderConfirmation.razor
    │   │   └── MyOrders.razor          # CUST### validation, sort, filter, expand
    │   ├── Services/
    │   │   ├── CartService.cs
    │   │   └── OrderApiService.cs
    │   ├── appsettings.json
    │   └── Program.cs
    │
    ├── AdminDashboard/                 # Blazor Server — operator-facing
    │   ├── Components/Pages/
    │   │   └── Orders.razor            # Live order table, status filter, sort
    │   ├── Services/
    │   │   └── AdminApiService.cs
    │   ├── appsettings.json
    │   └── Program.cs
    │
    └── tasks/
        ├── todo.md                     # Fix plan and checklist
        └── report.md                   # Post-fix verification report
```

---

## Assignment Rubric Coverage

| Criterion | Implementation |
|---|---|
| Microservices architecture | 6 independent services communicating via CloudAMQP events |
| Event-driven messaging | RabbitMQ fanout exchanges; durable queues; domain events per bounded context |
| REST API | OrderManagement.API with full CRUD + inventory check; Swagger/OpenAPI |
| Database persistence | EF Core 10, SQL Server LocalDB, code-first schema, `HasData` seed |
| Blazor Server UI (customer) | CustomerPortal — catalogue, cart, checkout with validation, order history |
| Blazor Server UI (admin) | AdminDashboard — live order monitoring, filter pills, sortable table |
| Structured logging | Serilog; Console + File + Seq sinks; correlation IDs across services |
| Configuration management | appsettings.json layering; .NET User Secrets; environment-aware overrides |
| Error handling & resilience | Fail-fast connection factory; Blazor circuit-safe exception handling; payment failure path |
| Code quality | Nullable reference types; implicit usings; consistent naming; XML doc comments |

---

*Submitted for Full Stack Development, Semester 2, Assignment 2.*
