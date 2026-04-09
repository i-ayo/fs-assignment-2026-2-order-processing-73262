# Lessons Learned — Engineering Patterns

## Lesson 1 — Always wire both ends of an event bus
**Pattern violated**: Added RabbitMQ publishers in workers but forgot to add consumers in the API.
**Consequence**: Orders stuck at InventoryPending forever in production.
**Rule**: For every exchange a worker PUBLISHES to, verify there is a corresponding CONSUMER somewhere that updates persistent state.
**Check**: Draw the full event flow end-to-end before marking a feature complete.

## Lesson 2 — Enum serialization must match on both sides
**Pattern violated**: API emits integer enum; client expects string → `JsonException`.
**Rule**: When mixing JSON across process boundaries, always apply `JsonStringEnumConverter` on BOTH the producing side (API `AddJsonOptions`) AND the consuming side (client `JsonSerializerOptions`).
**Check**: Write a regression test that proves the broken behavior and the fixed behavior.

## Lesson 3 — EF Core state machine needs explicit intermediate saves
**Pattern**: Wanting to record every state in history means two `SaveChangesAsync()` calls per transition.
**Rule**: If the spec says A → B → C, save after B, then save after C. Don't skip intermediate states even if they're transient.

## Lesson 4 — Write tool requires Read first
**Error**: "File has not been read yet. Read it first before writing to it."
**Rule**: ALWAYS read a file before attempting to write it, even for a complete rewrite.

## Lesson 5 — CI branch name: master not main
**Error**: Wrote `on: push: branches: [main]` — user's repo uses `master`.
**Rule**: Ask or check `git remote show origin` / `git branch` before hardcoding branch names.
**Default assumption for this project**: `master`.

## Lesson 6 — EnsureCreated vs Migrate
**Error**: Used `db.Database.Migrate()` with no migration files → runtime crash.
**Rule**: Use `EnsureCreated()` for demo projects without a Migrations folder. Never call `Migrate()` unless migration files exist.

## Lesson 7 — BackgroundService in API needs IServiceScopeFactory for EF Core
**Pattern**: EF Core `DbContext` is scoped; BackgroundService is singleton.
**Rule**: Always inject `IServiceScopeFactory`, not `OrderDbContext`, into a BackgroundService. Create a scope per message: `using var scope = factory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();`

## Lesson 8 — No-op fallback for RabbitMQ in CI
**Pattern**: CI has no RabbitMQ broker.
**Rule**: Every RabbitMQ consumer/publisher must check `config["RabbitMQ:Host"]` and gracefully become a no-op (await Task.Delay(Timeout.Infinite, ct)) when host is empty.

## Lesson 9 — Test assertions must match actual state machine behavior
**Error**: Test asserted `OrderStatus.InventoryConfirmed` as final state but endpoint transitions to `PaymentPending`.
**Rule**: After any state machine fix, update ALL related tests immediately. The test name should describe the ACTUAL end state, not the intermediate state.

## Lesson 10 — Never copy-paste a multi-value `applicationUrl` as a `BaseUrl`
**Error**: `AdminDashboard/appsettings.json` contained `"BaseUrl": "https://localhost:7100;https://localhost:5100"` — a copy of the API's `launchSettings.json` `applicationUrl` value which uses `;` to separate HTTPS and HTTP profiles.
**Consequence**: `new Uri("https://localhost:7100;https://localhost:5100")` is an invalid URI. The `HttpClient` base address was broken. `AdminApiService` catches all exceptions silently and returns empty lists → AdminDashboard shows no data with no visible error message.
**Rule**: `OrderApi:BaseUrl` must be a SINGLE valid absolute URI ending with `/`. Extract only the HTTPS URL. Example: `"https://localhost:7100/"`.
**Check**: After setting any BaseUrl, verify `new Uri(value)` does not throw by trying it in a unit test or confirming it matches the format `scheme://host:port/`.

## Lesson 11 — Keep all client BaseUrls consistent with the API's actual launchSettings port
**Error**: `CustomerPortal/appsettings.json` had `"BaseUrl": "https://localhost:7050/"` but the API launchSettings showed `https://localhost:7100`.
**Rule**: Whenever the API's launchSettings changes port, update ALL consumer appsettings (CustomerPortal, AdminDashboard) at the same time. Use a single source of truth — ideally document the port in the README or a shared `.env` file.
