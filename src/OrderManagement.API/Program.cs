using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using OrderManagement.API.Messaging;
using OrderManagement.API.Workers;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog structured logging ────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId());

// ── JSON: serialize enums as strings so the Blazor client can read them ───────
// FIX for Task A: OrderResponse.Status is an enum. Without JsonStringEnumConverter
// the API would emit an integer (e.g. 9) but the client model expects a string
// (e.g. "Completed"). Adding this converter here AND in the client fixes the
// "DeserializeUnableToConvertValue, System.String Path: $[0].status" error.
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Built-in .NET 10 OpenAPI support (replaces Swashbuckle) – serves /openapi/v1.json
builder.Services.AddOpenApi();

// ── EF Core ───────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<OrderDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("OrdersDb")));

// ── RabbitMQ publisher — crashes loudly if broker config is absent or wrong ────
// No silent fallback: if CloudAMQP credentials are missing the app throws on
// startup so the misconfiguration is immediately visible rather than silently
// leaving orders stuck at InventoryPending.
builder.Services.AddSingleton<IMessagePublisher>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var log = sp.GetRequiredService<ILogger<RabbitMqPublisher>>();
    // CreateAsync will throw InvalidOperationException if any credential is absent.
    var publisher = RabbitMqPublisher.CreateAsync(cfg, log).GetAwaiter().GetResult();
    log.LogInformation(
        "RabbitMQ publisher connected to {Host}:{Port}",
        cfg["RabbitMQ:Host"], cfg["RabbitMQ:Port"]);
    return publisher;
});

// ── OrderEventConsumer – bridges RabbitMQ result events → DB status updates ────
// This is the critical missing link: workers publish events to RabbitMQ but
// the API must listen to those exchanges and update order status in the database.
// Without this consumer, orders stay stuck at InventoryPending indefinitely.
builder.Services.AddHostedService<OrderEventConsumer>();

// ── CORS – allow all local origins for development ────────────────────────────
// Both frontends are Blazor Server so their HttpClient calls are server-to-server
// (CORS is only relevant for browser-initiated requests). We include all known
// local ports so any future browser-based client also works without changes.
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.WithOrigins(
        "https://localhost:7100",   // OrderManagement.API self (Swagger UI)
        "https://localhost:60869",  // CustomerPortal HTTPS (launchSettings)
        "http://localhost:60872",   // CustomerPortal HTTP  (launchSettings)
        "https://localhost:60873",  // AdminDashboard HTTPS (launchSettings)
        "http://localhost:60874",   // AdminDashboard HTTP  (launchSettings)
        "http://localhost:5100",
        "http://localhost:5200")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

Log.Information(
    "OrderManagement.API starting in {Environment}",
    app.Environment.EnvironmentName);

if (app.Environment.IsDevelopment())
{
    // Exposes /openapi/v1.json (viewable via Scalar or any OpenAPI client)
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

// ── Create schema and seed on startup ────────────────────────────────────────
// EnsureCreated() creates the database schema (including HasData seed rows) the
// first time the app runs, without requiring EF migration files.  Suitable for
// a student demo project where schema stability is not yet required.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
    Log.Information("OrderManagement database schema ensured");
}

Log.Information("OrderManagement.API is ready");
app.Run();
