# SportsStore — Lessons Learned

## Lesson 1: Always check project references in test projects
**What went wrong**: `InventoryStoreTests.cs` used `InventoryService.Models.InventoryStore` but
`OrderManagement.Tests.csproj` only referenced `OrderManagement.API` and `Shared.Domain`.
This caused a compile-time error that blocked the entire solution build.

**Rule**: When adding a test file that touches a project, immediately add that project to the test csproj.
Check all `using` statements at the top of every new test file and match each namespace to a referenced project.

---

## Lesson 2: HTTPS between two .NET processes in dev requires trusted dev certs
**What went wrong**: `AdminDashboard` and `CustomerPortal` called `https://localhost:7100/` (the API)
server-to-server. If `dotnet dev-certs https --trust` was never run, `HttpClient` throws
`HttpRequestException` (untrusted certificate). The `catch` block silently returns empty data,
giving the impression the API is returning nothing.

**Rule**: Always provide `appsettings.Development.json` in Blazor frontends that uses the plain-HTTP
address of the API (`http://localhost:PORT`). Reserve HTTPS URLs for production and docker-compose.
Never swallow HTTP exceptions silently in dev — log them at `Warning` level at minimum.

---

## Lesson 3: CORS comments must match actual ports
**What went wrong**: CORS `WithOrigins` listed `https://localhost:7100` labelled "CustomerPortal"
(that's actually the API itself) and `https://localhost:7200` labelled "AdminDashboard" (no such port).
The real ports from launchSettings are 60869 / 60873.

**Rule**: Derive CORS allowed-origins directly from `launchSettings.json`. Add a comment beside
each origin showing which project it belongs to and where the port was found.

---

## Lesson 4: Enum serialization must be consistent across ALL boundaries
**What went wrong**: `OrderStatus` is an enum. Without `JsonStringEnumConverter` the API emits
integers (e.g. `9`). Blazor clients trying to bind that `9` to `OrderStatus Status { get; set; }`
fail with `DeserializeUnableToConvertValue` because the default deserializer tries to parse
the JSON string `"Completed"` as an integer.

**Rule**: Register `JsonStringEnumConverter` in:
1. The API's `AddJsonOptions` (serialization)
2. Every client's `JsonSerializerOptions` (deserialization)
3. The EF model (`HasConversion<string>()`) to store human-readable values in the DB

---

## Lesson 5: Structured log context requires `LogContext.PushProperty`
**What went wrong**: Log messages included `{OrderId}` inline but the context was not pushed to
`LogContext`, so Seq/structured log sinks could not correlate events across services by OrderId.

**Rule**: At the start of every RabbitMQ handler, push `OrderId`, `CorrelationId`, `CustomerId`,
`ServiceName`, and `EventType` into `LogContext` using `using var _ = LogContext.PushProperty(...)`.
These then appear as structured fields in every log line for that message's lifetime.

---

## Lesson 6: `IJSRuntime` calls require a rendered Blazor circuit
**What went wrong**: Calling `JS.InvokeAsync` in `OnInitializedAsync` throws during prerendering
because no JS runtime is available.

**Rule**: Always do localStorage reads/writes in `OnAfterRenderAsync(firstRender: true)`, wrapped
in a try/catch that silently ignores JS interop failures (e.g. during SSR prerendering).
