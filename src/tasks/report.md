# Production Fix Report — CloudAMQP / LavinMQ Integration
**Date:** 2026-04-06
**Author:** Senior distributed-systems review pass
**Status:** ✅ All phases complete

---

## Root-Cause Summary

Orders were permanently stuck at `InventoryPending` because of a chain of three compounding failures:

1. **`appsettings.Development.json` overrode `RabbitMQ:Host` with an empty string.**
   `launchSettings.json` set `ASPNETCORE_ENVIRONMENT=Development` (the default), so the merged config always produced `Host=""`. The API registered `InMemoryPublisher` instead of `RabbitMqPublisher` and no events ever reached RabbitMQ.

2. **Workers silently entered no-op mode.**
   All three workers (`InventoryWorker`, `PaymentWorker`, `ShippingWorker`) and `OrderEventConsumer` checked `config["RabbitMQ:Host"]` and, if empty, called `Task.Delay(Timeout.Infinite)` — an invisible failure mode producing no crash, no error log, and no progress.

3. **`ConnectionFactory` was built with only `HostName`.**
   CloudAMQP requires TLS (port 5671), a `Username`, a `Password`, and a `VirtualHost`. Using a plain hostname-only factory caused an immediate TCP-level authentication rejection even when the host was correctly set.

---

## Fixes Applied

### Phase 1 — appsettings.json (all services)

| File | Change |
|------|--------|
| `OrderManagement.API/appsettings.json` | `Host` → `ostrich.lmq.cloudamqp.com`, `Port` 5671, TLS, vhost=evilufrt |
| `OrderManagement.API/appsettings.Development.json` | Replaced blank `Host` with full CloudAMQP config — eliminates the override bug |
| `OrderManagement.API/Properties/launchSettings.json` | Default profile → `ASPNETCORE_ENVIRONMENT=Production` |
| `InventoryService/appsettings.json` | Full CloudAMQP config |
| `PaymentService/appsettings.json` | Full CloudAMQP config |
| `ShippingService/appsettings.json` | Full CloudAMQP config |

### Phase 2 — TLS ConnectionFactory helpers (created)

Four new `RabbitMqConnectionFactory.cs` files (one per service namespace):
- `OrderManagement.API.Messaging.RabbitMqConnectionFactory`
- `InventoryService.Messaging.RabbitMqConnectionFactory`
- `PaymentService.Messaging.RabbitMqConnectionFactory`
- `ShippingService.Messaging.RabbitMqConnectionFactory`

Each helper:
- Reads Host, Username, Password, VirtualHost, Port, UseTls from config
- **Throws `InvalidOperationException`** loudly if any credential is missing or still set to `REPLACE_ME`
- Builds `ConnectionFactory` with Port=5671, `SslOption { Enabled=true, ServerName=host, AcceptablePolicyErrors=None }` when `UseTls=true`
- Enables `AutomaticRecoveryEnabled=true` and `NetworkRecoveryInterval=10s`

### Phase 3 — API publisher and consumer

| File | Change |
|------|--------|
| `RabbitMqPublisher.CreateAsync(string hostName, …)` | Signature changed to `CreateAsync(IConfiguration config, …)`; uses TLS factory helper |
| `Program.cs` | Removed silent `InMemoryPublisher` fallback; crashes loudly on connection failure; passes `IConfiguration` |
| `OrderEventConsumer.ExecuteAsync` | Removed `RabbitMQ:Host` empty-check + no-op path; uses `RabbitMqConnectionFactory.Create(config)` |

### Phase 4 — Worker services

| Worker | Change |
|--------|--------|
| `InventoryWorker` | Added `using InventoryService.Messaging`; removed no-op check; uses `RabbitMqConnectionFactory.Create(config)` |
| `PaymentWorker` | Added `using PaymentService.Messaging`; removed no-op check; uses `RabbitMqConnectionFactory.Create(config)` |
| `ShippingWorker` | Added `using ShippingService.Messaging`; removed no-op check; uses `RabbitMqConnectionFactory.Create(config)` |

### Phase 5 — Serilog version alignment

`PaymentService.csproj`: `Serilog.Extensions.Hosting` upgraded from `9.0.0` → `10.0.0` to match `InventoryService` and `ShippingService`.

### Phase 6 — Hero banner restored

`CustomerPortal/Components/Pages/Products.razor`: Full hero banner re-added above the category filter bar:
- Gradient background (`#1C1C1E → #3a3a5c → #6366f1`)
- Badge, headline with accent, subtitle, "Shop All Products" CTA
- Decorative emoji cluster (⚽ 🚣 ♟️)

---

## Verification Results

| Check | Result |
|-------|--------|
| No bare `"localhost"` in any `appsettings.json` (except `ConnectionStrings`) | ✅ CLEAN |
| No `no-op` / early `Task.Delay(Timeout.Infinite)` in workers | ✅ CLEAN |
| No hostname-only `new ConnectionFactory` outside factory helper | ✅ CLEAN |
| `RabbitMqConnectionFactory.Create(config)` called in all 5 consumers/publishers | ✅ 5/5 |
| TLS factory file present in all 4 services | ✅ 4/4 |
| `EventType` structured log property in all worker handlers (8 total) | ✅ 8/8 |
| `Serilog.Extensions.Hosting` v10.0.0 in PaymentService | ✅ Fixed |
| Hero banner markup in `Products.razor` | ✅ Restored |

---

## Expected Pipeline Flow (after password is set)

Once `REPLACE_ME` is replaced with the real CloudAMQP password in each `appsettings.json`:

```
Place Order → Submitted
  → [order.submitted exchange]
    → InventoryWorker
      ✅ → [inventory.confirmed] → OrderEventConsumer → InventoryConfirmed → PaymentPending
        → PaymentWorker
          ✅ (90%) → [payment.approved] → OrderEventConsumer → PaymentApproved → ShippingPending
            → ShippingWorker
              ✅ → [shipping.created] → OrderEventConsumer → ShippingCreated → Completed
          ❌ (10%) → [payment.failed] → OrderEventConsumer → PaymentFailed
      ❌ → [inventory.failed] → OrderEventConsumer → InventoryFailed
```

All transitions produce structured Serilog logs with `OrderId`, `CustomerId`, `CorrelationId`, `EventType`, and `ServiceName` properties visible in Seq at `http://localhost:5341`.

---

## Remaining Action

**Set the real password** — replace `REPLACE_ME` with the actual CloudAMQP password in:
- `OrderManagement.API/appsettings.json`
- `OrderManagement.API/appsettings.Development.json`
- `InventoryService/appsettings.json`
- `PaymentService/appsettings.json`
- `ShippingService/appsettings.json`

Use a user secret (`dotnet user-secrets set "RabbitMQ:Password" "…"`) or an environment variable (`RabbitMQ__Password`) to avoid committing credentials to source control.
