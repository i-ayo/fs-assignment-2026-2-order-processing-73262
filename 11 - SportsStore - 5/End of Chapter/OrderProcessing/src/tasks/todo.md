# Fix Plan — CloudAMQP / LavinMQ Production Pipeline
Generated: 2026-04-06

---

## ROOT-CAUSE ANALYSIS

After scanning every file in the repo, the following defects cause orders to be permanently stuck at **InventoryPending**:

### CRITICAL Bug #1 — API NEVER publishes to RabbitMQ (Development override)
`OrderManagement.API/appsettings.Development.json` contains:
```json
"RabbitMQ": { "Host": "" }
```
`launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, so .NET merges this override on top of `appsettings.json`, **blanking the Host**. The API falls back to `InMemoryPublisher` (no-op), meaning it NEVER sends `OrderSubmittedEvent` to the broker. Workers never receive a message.

### CRITICAL Bug #2 — All services point to `localhost`, not CloudAMQP
Every `appsettings.json` has `"Host": "localhost"`. CloudAMQP is at `ostrich.lmq.cloudamqp.com:5671`.

### CRITICAL Bug #3 — No TLS, no credentials, no VirtualHost in ConnectionFactory
`RabbitMqPublisher`, `OrderEventConsumer`, `InventoryWorker`, `PaymentWorker`, `ShippingWorker` all build `ConnectionFactory` with only `HostName`. CloudAMQP requires:
- Port 5671 (AMQPS)
- SSL/TLS (SslOption.Enabled = true)
- Username = "evilufrt"
- Password = (secret)
- VirtualHost = "evilufrt"

Without these, every connection to CloudAMQP fails with an auth/TLS error and the service silently falls back to no-op or crashes.

### CRITICAL Bug #4 — Workers silently go no-op instead of crashing
Workers check `config["RabbitMQ:Host"]` and if blank they enter an infinite `Task.Delay`. They should throw and crash loudly so the problem is visible in logs.

### Bug #5 — `RabbitMqPublisher.CreateAsync` only accepts `hostName`
The method signature is `CreateAsync(string hostName, ...)`. It must accept the full `IConfiguration` section to build a TLS-authenticated connection.

### Bug #6 — API Program.cs passes only `rabbitHost` string to publisher
Feeds only the hostname string — no port, no TLS, no auth.

### Bug #7 — `OrderEventConsumer` reads only `RabbitMQ:Host`
Same single-key config read, same broken factory.

### Bug #8 — Hero banner removed from Products.razor
Previous session removed the hero section. Requirement says restore it.

### Bug #9 — `appsettings.Development.json` for API not updated
Even if we fix `appsettings.json`, the Development override will always blank the Host.

### Bug #10 — launchSettings.json sets Development environment
The API runs in Development, which triggers Bug #9.

### Bug #11 — PaymentService.csproj references old Serilog.Extensions.Hosting v9.0.0
Should be 10.0.0 to match the other services.

---

## COMPLETE FIX CHECKLIST

### Phase 1 — Configuration (appsettings)
- [x] A1. `OrderManagement.API/appsettings.json` — replace RabbitMQ section with full CloudAMQP config
- [x] A2. `OrderManagement.API/appsettings.Development.json` — remove empty Host override; add full CloudAMQP config
- [x] A3. `OrderManagement.API/Properties/launchSettings.json` — set `ASPNETCORE_ENVIRONMENT=Production`
- [x] A4. `InventoryService/appsettings.json` — CloudAMQP config
- [x] A5. `PaymentService/appsettings.json` — CloudAMQP config
- [x] A6. `ShippingService/appsettings.json` — CloudAMQP config

### Phase 2 — Shared Connection Helper
- [x] B1. Create `OrderManagement.API/Messaging/RabbitMqConnectionFactory.cs` — single helper that builds a TLS-authenticated `ConnectionFactory` from `IConfiguration`
- [x] B2. Create `InventoryService/Messaging/RabbitMqConnectionFactory.cs` — same helper for workers
- [x] B3. Create `PaymentService/Messaging/RabbitMqConnectionFactory.cs`
- [x] B4. Create `ShippingService/Messaging/RabbitMqConnectionFactory.cs`

### Phase 3 — Fix API Messaging Layer
- [x] C1. `RabbitMqPublisher.cs` — update `CreateAsync` to accept `IConfiguration`, build TLS factory
- [x] C2. `OrderManagement.API/Program.cs` — pass `IConfiguration` instead of hostname string; remove silent fallback (crash loudly on CloudAMQP failure)
- [x] C3. `OrderEventConsumer.cs` — use full TLS factory; crash loudly if config missing; add EventType structured logs for all 5 event types

### Phase 4 — Fix Worker Services
- [x] D1. `InventoryWorker.cs` — use TLS factory; crash loudly if host/creds missing; add structured EventType logs for OrderSubmitted, InventoryConfirmed, InventoryFailed
- [x] D2. `PaymentWorker.cs` — use TLS factory; crash loudly; add structured logs for PaymentApproved, PaymentFailed
- [x] D3. `ShippingWorker.cs` — use TLS factory; crash loudly; add structured logs for ShippingCreated, OrderCompleted

### Phase 5 — Fix csproj
- [x] E1. `PaymentService.csproj` — upgrade Serilog.Extensions.Hosting to 10.0.0

### Phase 6 — UI Fix
- [x] F1. `CustomerPortal/Components/Pages/Products.razor` — restore original hero banner

### Phase 7 — Verification
- [x] G1. Verify all appsettings have identical CloudAMQP config
- [x] G2. Verify no service uses `localhost` for RabbitMQ
- [x] G3. Verify all ConnectionFactory instances use TLS + auth + vhost
- [x] G4. Verify no silent no-op fallback remains
- [x] G5. Verify EventType logs present in all workers and consumers
- [x] G6. Write final report to `tasks/report.md`

---

## CloudAMQP Config (canonical — used in ALL services)
```json
"RabbitMQ": {
  "Host": "ostrich.lmq.cloudamqp.com",
  "Port": 5671,
  "Username": "evilufrt",
  "Password": "REPLACE_ME",
  "VirtualHost": "evilufrt",
  "UseTls": true
}
```
> ⚠️  Replace `REPLACE_ME` with the actual password before running.

## Exchange & Queue Map (canonical — all names must be IDENTICAL across services)
| Exchange             | Type   | Queue (consumer)                  | Consumer            |
|----------------------|--------|-----------------------------------|---------------------|
| `order.submitted`    | fanout | `inventory.order.submitted`       | InventoryWorker     |
| `inventory.confirmed`| fanout | `payment.inventory.confirmed`     | PaymentWorker       |
| `inventory.confirmed`| fanout | `api.inventory.confirmed`         | OrderEventConsumer  |
| `inventory.failed`   | fanout | `api.inventory.failed`            | OrderEventConsumer  |
| `payment.approved`   | fanout | `shipping.payment.approved`       | ShippingWorker      |
| `payment.approved`   | fanout | `api.payment.approved`            | OrderEventConsumer  |
| `payment.failed`     | fanout | `api.payment.failed`              | OrderEventConsumer  |
| `shipping.created`   | fanout | `api.shipping.created`            | OrderEventConsumer  |

All exchanges are Fanout + durable. All queues are durable.
