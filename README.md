# SportsStore — Distributed Order Processing Platform

> Full Stack Development · Semester 2 · Assignment 2

[![CI — Build & Test](https://github.com/i-ayo/fs-assignment-2026-2-order-processing-73262/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/i-ayo/fs-assignment-2026-2-order-processing-73262/actions/workflows/ci.yml)
[![CD — Docker Build & Publish](https://github.com/i-ayo/fs-assignment-2026-2-order-processing-73262/actions/workflows/deploy.yml/badge.svg?branch=master)](https://github.com/i-ayo/fs-assignment-2026-2-order-processing-73262/actions/workflows/deploy.yml)

---

## Table of Contents

1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [Event Flow](#event-flow)
4. [Service Responsibilities](#service-responsibilities)
5. [Technology Stack](#technology-stack)
6. [Project Structure](#project-structure)
7. [How to Run](#how-to-run)
   - [Local Development (Visual Studio / CLI)](#local-development)
   - [Docker Compose (Full Stack)](#docker-compose)
8. [CI/CD Pipeline](#cicd-pipeline)
9. [Structured Logging (Serilog)](#structured-logging)
10. [CQRS with MediatR](#cqrs-with-mediatr)
11. [AutoMapper](#automapper)
12. [Database Schema](#database-schema)
13. [Assumptions and Limitations](#assumptions-and-limitations)

---

## Overview

This platform extends the SportsStore shopping cart into a **distributed, event-driven order processing system**. When a customer checks out, the order flows asynchronously through three independent microservices (Inventory → Payment → Shipping) coordinated via **RabbitMQ**, with every state transition persisted to a SQL Server database and surfaced to two separate UIs.

---

## System Architecture

```
Customer Browser
      │
      ▼
 CustomerPortal          AdminDashboard
 (Blazor Server)         (Blazor Server)
      │                        │
      │   HTTP (REST)          │  HTTP (REST)
      └──────────┬─────────────┘
                 ▼
       OrderManagement.API
       (.NET 10 · MediatR · EF Core · SQL Server)
                 │
                 │  Publishes to RabbitMQ (CloudAMQP / TLS)
                 ▼
         order.submitted  (fanout)
         ┌───────────────────────┐
         ▼                       ▼
  InventoryService         (other subscribers)
         │
         │ inventory.confirmed / inventory.failed
         ▼
  PaymentService
         │
         │ payment.approved / payment.failed
         ▼
  ShippingService
         │
         │ shipping.created
         ▼
  OrderEventConsumer (inside OrderManagement.API)
         │
         ▼
  SQL Server — order status updated
```

---

## Event Flow

| Step | Exchange | Publisher | Consumer | Order Status |
|------|----------|-----------|----------|-------------|
| 1 | `order.submitted` | OrderManagement.API | InventoryService | `InventoryPending` |
| 2a | `inventory.confirmed` | InventoryService | OrderEventConsumer + PaymentService | `InventoryConfirmed` → `PaymentPending` |
| 2b | `inventory.failed` | InventoryService | OrderEventConsumer | `InventoryFailed` |
| 3a | `payment.approved` | PaymentService | OrderEventConsumer + ShippingService | `PaymentApproved` → `ShippingPending` |
| 3b | `payment.failed` | PaymentService | OrderEventConsumer | `PaymentFailed` |
| 4a | `shipping.created` | ShippingService | OrderEventConsumer | `ShippingCreated` → `Completed` |
| 4b | `shipping.failed` | ShippingService | OrderEventConsumer | `Failed` |

All exchanges are **fanout** type with **durable queues** and **persistent messages** so no event is lost on a service restart.

---

## Service Responsibilities

### OrderManagement.API
- Accepts checkout requests via `POST /api/orders/checkout`
- Persists orders to SQL Server via EF Core
- Publishes `OrderSubmittedEvent` to RabbitMQ
- Hosts `OrderEventConsumer` background service that bridges RabbitMQ result events back to database status updates
- Exposes REST endpoints for both frontends
- Implements CQRS via MediatR and DTO mapping via AutoMapper

### InventoryService
- Consumes `order.submitted` exchange
- Checks in-memory stock via `InventoryStore` (atomic, thread-safe)
- Publishes `InventoryConfirmedEvent` or `InventoryFailedEvent`

### PaymentService
- Consumes `inventory.confirmed` exchange
- Simulates payment authorization (approves most, randomly rejects ~10%)
- Publishes `PaymentApprovedEvent` or `PaymentFailedEvent`

### ShippingService
- Consumes `payment.approved` exchange
- Generates a tracking reference (`TRK-{date}-{orderId[..7]}`)
- Publishes `ShippingCreatedEvent`

### CustomerPortal (Blazor Server)
- Hero landing page, product catalogue with category/search/sort
- Shopping cart with live badge (Scoped `CartService`, `StateChanged` event)
- Checkout with `CUST001`–`CUST999` format validation and known-customer auto-fill
- My Orders with `localStorage` persistence, expandable rows, and status filters

### AdminDashboard (Blazor Server)
- Live metrics dashboard (auto-refreshes every 10 s)
- Full orders table: filter by status/customer, sort all columns, click-to-expand detail rows showing order lines, tracking, payment ref, and failure reason
- Revenue summary for completed orders

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 |
| API framework | ASP.NET Core 10 Web API |
| Frontend | Blazor Server (InteractiveServer, prerender disabled) |
| ORM | Entity Framework Core 10 + SQL Server |
| Messaging | RabbitMQ 3 (CloudAMQP, TLS port 5671, fanout exchanges) |
| CQRS | MediatR 12 |
| Mapping | AutoMapper 12 |
| Logging | Serilog 10 (Console + rolling File + Seq sinks) |
| Containers | Docker + Docker Compose |
| CI/CD | GitHub Actions (.NET 10 SDK) |

---

## Project Structure

```
11 - SportsStore - 5/End of Chapter/OrderProcessing/
├── OrderProcessing.sln
├── docker-compose.yml
├── src/
│   ├── Shared.Domain/          # Enums, events, DTOs shared across all services
│   ├── OrderManagement.API/    # Central API: CQRS handlers, EF Core, RabbitMQ publisher
│   │   ├── Controllers/        # Thin controllers — delegate to MediatR
│   │   ├── Features/           # Commands + Queries (CQRS)
│   │   ├── Profiles/           # AutoMapper mapping profiles
│   │   ├── Data/               # DbContext, entities, seed data
│   │   ├── Messaging/          # RabbitMqPublisher
│   │   └── Workers/            # OrderEventConsumer (hosted background service)
│   ├── InventoryService/       # BackgroundService — consumes order.submitted
│   ├── PaymentService/         # BackgroundService — consumes inventory.confirmed
│   ├── ShippingService/        # BackgroundService — consumes payment.approved
│   ├── CustomerPortal/         # Blazor Server — customer-facing UI
│   └── AdminDashboard/         # Blazor Server — admin/ops UI
└── tests/
    └── OrderManagement.Tests/  # xUnit: 26 tests covering controllers, state machine, serialization
```

---

## How to Run

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) or Docker
- RabbitMQ (local or CloudAMQP — see credentials below)
- _(Optional)_ [Seq](https://datalust.co/seq) on `http://localhost:5341` for structured log UI

### Local Development

**1. Set the RabbitMQ password**

Create `src/OrderManagement.API/appsettings.Local.json` (gitignored):
```json
{
  "RabbitMQ": {
    "Password": "<your-cloudamqp-password>"
  }
}
```
Do the same for `InventoryService`, `PaymentService`, and `ShippingService`.

**2. Start all services** (open five terminals or use Visual Studio multi-project launch):

```bash
# Terminal 1 — API
cd src/OrderManagement.API && dotnet run

# Terminal 2 — Inventory
cd src/InventoryService && dotnet run

# Terminal 3 — Payment
cd src/PaymentService && dotnet run

# Terminal 4 — Shipping
cd src/ShippingService && dotnet run

# Terminal 5 — Customer Portal
cd src/CustomerPortal && dotnet run

# Terminal 6 — Admin Dashboard
cd src/AdminDashboard && dotnet run
```

**3. Open in browser**

| Service | URL |
|---------|-----|
| Customer Portal | https://localhost:60869 |
| Admin Dashboard | https://localhost:60873 |
| Order API (OpenAPI) | https://localhost:7100/openapi/v1.json |

**4. Run tests**

```bash
cd "11 - SportsStore - 5/End of Chapter/OrderProcessing"
dotnet test OrderProcessing.sln --configuration Release --verbosity normal
```

---

### Docker Compose

> Docker must be running. Builds all six service images locally using the multi-stage Dockerfiles.

```bash
cd "11 - SportsStore - 5/End of Chapter/OrderProcessing"
docker compose up --build
```

Services exposed on the host:

| Service | Host Port |
|---------|-----------|
| OrderManagement.API | http://localhost:5050 |
| CustomerPortal | http://localhost:5100 |
| AdminDashboard | http://localhost:5200 |
| RabbitMQ Management UI | http://localhost:15672 (guest/guest) |
| SQL Server | localhost:1433 |

> **Note:** The Docker Compose configuration uses local RabbitMQ (not CloudAMQP). For production, replace `RabbitMQ__Host` with your CloudAMQP host and supply credentials via environment variables or Docker secrets.

---

## CI/CD Pipeline

Two GitHub Actions workflows run on every push to `master` and every pull request:

### `ci.yml` — Build & Test
1. **Restore** — `dotnet restore`
2. **Build** — `dotnet build --configuration Release`
3. **Test** — `dotnet test` with TRX logger + XPlat code coverage
4. **Artifacts** — uploads `.trx` result files and `coverage.cobertura.xml`
5. **Test report** — `dorny/test-reporter` publishes test results inline on the PR

Pipeline **fails immediately** if any test fails.

### `deploy.yml` — Docker Build & Publish
- Triggers only when `CI — Build & Test` succeeds on `master`
- Builds all six Docker images using the multi-stage Dockerfiles
- Pushes to **GitHub Container Registry** (`ghcr.io`) tagged with both the short commit SHA and `latest`

---

## Structured Logging (Serilog)

All services use `Serilog.AspNetCore` configured via `appsettings.json`:

```json
"WriteTo": [
  { "Name": "Console",  "Args": { "outputTemplate": "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}" } },
  { "Name": "File",     "Args": { "path": "logs/<service>-.log", "rollingInterval": "Day" } },
  { "Name": "Seq",      "Args": { "serverUrl": "http://localhost:5341" } }
],
"Enrich": [ "FromLogContext", "WithMachineName", "WithEnvironmentName", "WithThreadId" ]
```

Every event handler pushes structured context properties via `LogContext.PushProperty`:

| Property | Example Value |
|----------|-------------|
| `OrderId` | `3fa85f64-5717-...` |
| `CorrelationId` | `c1a2b3d4-...` |
| `CustomerId` | `CUST001` |
| `EventType` | `InventoryConfirmed` |
| `ServiceName` | `InventoryService` |
| `MachineName` | `ALIEN` |
| `EnvironmentName` | `Development` |

Start Seq locally (`docker run -e ACCEPT_EULA=Y -p 5341:80 datalust/seq`) to view all events with full property filtering.

---

## CQRS with MediatR

`OrderManagement.API` implements full CQRS separation:

**Commands** (write side — mutate state):
- `CheckoutOrderCommand` — creates order, publishes `OrderSubmittedEvent`
- `ProcessInventoryResultCommand` — transitions `InventoryPending` → `InventoryConfirmed/Failed`
- `ProcessPaymentResultCommand` — transitions `PaymentPending` → `PaymentApproved/Failed`
- `CreateShipmentCommand` — transitions `ShippingPending` → `Completed`

**Queries** (read side — no side effects):
- `GetOrderByIdQuery`
- `GetOrdersQuery` — supports status and customer ID filters
- `GetCustomerOrdersQuery`
- `GetDashboardSummaryQuery`

Controllers are thin delegating wrappers — all business logic lives in handlers.

---

## AutoMapper

`OrderMappingProfile` maps:

| Source | Destination | Notes |
|--------|------------|-------|
| `Order` | `OrderResponse` | Convention-based |
| `OrderLine` | `OrderLineResponse` | Computes `LineTotal = Qty × UnitPrice` |

Registered via `builder.Services.AddAutoMapper(typeof(OrderMappingProfile))` (AutoMapper 12 + Extensions 12, compatible pair).

---

## Database Schema

SQL Server (`(localdb)\MSSQLLocalDB`, database `OrderProcessing`) managed by EF Core with `EnsureCreated()` on startup.

| Table | Key Columns |
|-------|------------|
| `Orders` | `Id` (Guid), `CustomerId`, `CustomerName`, `ShippingAddress`, `Status` (string), `TotalAmount`, `FailureReason`, `PaymentTransactionId`, `TrackingNumber`, `CreatedAt`, `UpdatedAt` |
| `OrderLines` | `Id`, `OrderId` (FK), `ProductId`, `ProductName`, `Quantity`, `UnitPrice` |

Seed data covers 11 orders across all order statuses for demo and testing purposes.

---

## Assumptions and Limitations

| Area | Note |
|------|------|
| Authentication | Not implemented — out of scope for this assignment |
| InventoryService stock | In-memory `InventoryStore`; resets on restart. Production would use a persistent database |
| PaymentService | Simulated — approves ~90% of payments, randomly rejects ~10% |
| RabbitMQ credentials | Real password stored in gitignored `appsettings.Local.json`; placeholder `"Secrets"` in committed config |
| SQL Server | Requires LocalDB for local dev; Docker Compose uses full SQL Server 2022 image |
| Seq | Optional — all logs also go to Console and rolling file. Seq provides the structured log UI |
| Docker images | Built from source in the CI/CD pipeline. No pre-built images are hosted publicly |
